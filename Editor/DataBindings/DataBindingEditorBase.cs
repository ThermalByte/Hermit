using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using Hermit.DataBinding;
using Hermit.Helpers;
using Hermit.Views;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

namespace Hermit.DataBindings
{
    public abstract class DataBindingEditorBase : Editor
    {
        protected static readonly Dictionary<Type, List<Type>> AdapterLookup = new Dictionary<Type, List<Type>>();

        protected static readonly Dictionary<Type, AdapterAttribute> AdapterAttributeLookup =
            new Dictionary<Type, AdapterAttribute>();

        protected static List<Type> ViewCollectionChangedHandlerTypes = new List<Type>();

        protected readonly Func<Type, string> AdapterFromName = type => AdapterAttributeLookup[type].FromType.Name;
        protected readonly Func<Type, string> AdapterToName = type => AdapterAttributeLookup[type].ToType.Name;

        #region Styles

        protected bool StyleInitialized;

        protected GUIStyle BindingTypeLabelStyle;
        protected GUIStyle BindingLabelContainerStyle;
        protected GUIStyle AlignToRightStyle;

        #endregion

        protected DataBindingBase BindingBase;

        protected Type ViewSource;
        protected Type ViewDest;

        protected Type ViewModelSource;
        protected Type ViewModelDest;

        protected MemberInfo BindingEvent;

        protected Type ViewCurrentType => ViewDest != null ? ViewDest : ViewSource;
        protected Type ViewModelCurrentType => ViewModelDest != null ? ViewModelDest : ViewModelSource;

        protected readonly string[] InternalMethodFilter = {"OnPropertyChanged", "OnPropertyChanging"};

        protected virtual void OnEnable()
        {
            BindingBase = target as DataBindingBase;

            if (AdapterLookup.Count <= 0) { CollectAdapters(); }

            if (ViewCollectionChangedHandlerTypes.Count <= 0) { CollectViewHandlers(); }
        }

        public override void OnInspectorGUI()
        {
            if (!StyleInitialized) { SetupStyles(); }

            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                var providerProperty = nameof(BindingBase.dataProviderComponent);
                EditorGUILayout.PropertyField(serializedObject.FindProperty(providerProperty));
            }
        }

        private void SetupStyles()
        {
            StyleInitialized = true;

            if (AlignToRightStyle == null)
            {
                AlignToRightStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleRight
                };
            }

            if (BindingTypeLabelStyle == null)
            {
                BindingTypeLabelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    normal = {textColor = EditorStyles.label.normal.textColor},
                    richText = true,
                    fontSize = 11,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            if (BindingLabelContainerStyle == null)
            {
                BindingLabelContainerStyle = new GUIStyle("ShurikenModuleTitle")
                {
                    font = new GUIStyle(EditorStyles.label).font,
                    border = new RectOffset(15, 7, 4, 4),
                    fixedHeight = 24
                };
            }
        }

        private static void CollectAdapters()
        {
            var adapters = AssemblyHelper.GetInheritancesInAppDomain(typeof(IAdapter)).ToList();
            foreach (var adapter in adapters)
            {
                var adapterAttribute = adapter.GetCustomAttribute<AdapterAttribute>();
                if (adapterAttribute == null)
                {
                    Debug.LogError($"{adapter} doesn't decorated with [Adapter] attribute.");
                    continue;
                }

                AdapterAttributeLookup.Add(adapter, adapterAttribute);
                if (AdapterLookup.TryGetValue(adapterAttribute.FromType, out var results)) { results.Add(adapter); }
                else { AdapterLookup[adapterAttribute.FromType] = new List<Type> {adapter}; }
            }
        }

        protected void CollectViewHandlers()
        {
            ViewCollectionChangedHandlerTypes = AssemblyHelper
                .GetInheritancesInAppDomain(typeof(IViewCollectionChangedHandler)).ToList();
        }

        #region View Model

        protected string DrawViewModelPopup(string viewModelEntry)
        {
            // TODO: Cache mapping
            var dataProviders = BindingBase.transform.GetComponentsInParent<IViewModelProvider>(true);

            if (dataProviders.Length <= 0)
            {
                var oriCol = EditorStyles.label.normal.textColor;
                EditorStyles.label.normal.textColor = Color.red;

                EditorGUILayout.LabelField("View Model not found in context.");

                EditorStyles.label.normal.textColor = oriCol;
                return null;
            }

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var viewModelTypes = new List<Type>();
            var providerLookup = new Dictionary<Type, IViewModelProvider>();

            foreach (var dataProvider in dataProviders)
            {
                foreach (var assembly in assemblies)
                {
                    var viewModelType = assembly.GetType(dataProvider.ViewModelTypeName);
                    if (viewModelType == null) { continue; }

                    viewModelTypes.Add(viewModelType);
                    providerLookup.Add(viewModelType, dataProvider);
                }
            }

            var viewModelPropertyInfos =
                viewModelTypes.ToDictionary(key => key, value => value.GetProperties().ToList());

            var options = new List<string>();
            var lookup = new List<string>();
            var viewModelTypeLookup = new List<Type>();
            var propertyTypeLookup = new List<Type>();

            foreach (var info in viewModelPropertyInfos)
            {
                var properties = info.Value.Select(p =>
                {
                    var provider = (Component) providerLookup[info.Key];
                    var property =
                        $"{provider.name}/{info.Key.PrettyName()}/{p.Name} - [{p.PropertyType.PrettyName()}]";
                    return property;
                });
                var items = info.Value.Select(p => $"{info.Key.FullName}.{p.Name}");
                var vmType = info.Value.Select(p => info.Key);
                var pis = info.Value.Select(p => p.PropertyType);

                options.AddRange(properties);
                lookup.AddRange(items);
                viewModelTypeLookup.AddRange(vmType);
                propertyTypeLookup.AddRange(pis);
            }

            var selection = lookup.IndexOf(viewModelEntry);

            // Select View Model
            using var check = new EditorGUI.ChangeCheckScope();
            selection = EditorGUILayout.Popup("Property", selection, options.ToArray());

            if (ViewModelSource == null && selection >= 0) { ViewModelSource = propertyTypeLookup[selection]; }

            if (check.changed)
            {
                viewModelEntry = lookup[selection];
                BindingBase.dataProviderComponent = providerLookup[viewModelTypeLookup[selection]] as Component;

                ViewModelSource = propertyTypeLookup[selection];
                ViewModelDest = null;
            }

            return viewModelEntry;
        }

        #endregion

        #region View

        protected virtual string DrawViewPropertyPopup(string viewEntry)
        {
            var components = BindingBase.gameObject.GetComponents<Component>().ToList();
            var data = new List<Type>();
            var options = new List<string>();
            var lookup = new List<string>();

            // Fill data
            foreach (var component in components)
            {
                if (component == BindingBase || component == null) { continue; }

                var bindingFlags = BindingFlags.Instance | BindingFlags.Public;

                // TODO: configs for static properties
                // Get properties with public setter and getter
                var properties = component.GetType()
                    .GetProperties(bindingFlags)
                    .Where(p => p.GetGetMethod(false) != null && p.GetSetMethod(false) != null);

                var fields = component.GetType()
                    .GetFields(bindingFlags);

                foreach (var propertyInfo in properties)
                {
                    data.Add(propertyInfo.PropertyType);
                    lookup.Add($"{component.GetType()}.{propertyInfo.Name}");
                    options.Add(
                        $"{component.GetType().PrettyName()}/{propertyInfo.Name} - [{propertyInfo.PropertyType.PrettyName()}]");
                }

                foreach (var fieldInfo in fields)
                {
                    data.Add(fieldInfo.FieldType);
                    lookup.Add($"{component.GetType()}.{fieldInfo.Name}");
                    options.Add(
                        $"{component.GetType().PrettyName()}/{fieldInfo.Name} - [{fieldInfo.FieldType.PrettyName()}]");
                }
            }

            var selection = lookup.IndexOf(viewEntry);
            using var check = new EditorGUI.ChangeCheckScope();
            selection = EditorGUILayout.Popup("Members", selection, options.ToArray());

            if (ViewSource == null && selection >= 0) { ViewSource = data[selection]; }

            if (!check.changed) { return viewEntry; }

            ViewSource = data[selection];
            return lookup[selection];
        }

        protected virtual string DrawViewMethodPopup(string viewEntry, bool declaredMethodsOnly)
        {
            var components = BindingBase.gameObject.GetComponents<Component>().ToList();
            var data = new List<Type>();
            var options = new List<string>();
            var lookup = new List<string>();

            // Fill data
            foreach (var component in components)
            {
                if (component == BindingBase) { continue; }

                var type = component.GetType();
                var propertyAccessors = type.GetProperties(BindingFlags.Static | BindingFlags.Public)
                    .SelectMany(p => p.GetAccessors());

                var eventAccessors = type.GetEvents(BindingFlags.Static | BindingFlags.Public)
                    .SelectMany(e => new[]
                    {
                        e.GetAddMethod(true),
                        e.GetRemoveMethod(true)
                    });

                var accessors = propertyAccessors.Concat(eventAccessors);

                var filter = BindingFlags.Instance | BindingFlags.Public;
                if (declaredMethodsOnly) { filter |= BindingFlags.DeclaredOnly; }

                var methodInfos = type
                    .GetMethods(filter).Except(accessors)
                    .Where(m => !m.IsSpecialName && m.GetParameters().Length <= 1);

                foreach (var methodInfo in methodInfos)
                {
                    var parameter = methodInfo.GetParameters();
                    var parameterType = parameter.Length == 1 ? parameter[0].ParameterType : typeof(void);
                    data.Add(parameterType);
                    lookup.Add($"{component.GetType()}.{methodInfo.Name}");
                    options.Add(
                        $"{component.GetType().PrettyName()}/{methodInfo.Name} - [{parameterType.PrettyName()}]");
                }
            }

            var selection = lookup.IndexOf(viewEntry);
            using var check = new EditorGUI.ChangeCheckScope();
            selection = EditorGUILayout.Popup("Methods", selection, options.ToArray());

            if (ViewSource == null && selection >= 0) { ViewSource = data[selection]; }

            if (!check.changed) { return viewEntry; }

            ViewSource = data[selection];
            return lookup[selection];
        }

        #endregion

        #region View Action

        protected virtual (string eventEntry, MemberInfo memberInfo) DrawViewMethodsPopup(string viewMethodEntry,
            bool declaredMethodsOnly)
        {
            var components = BindingBase.gameObject.GetComponents<Component>().ToList();
            var data = new List<MemberInfo>();
            var options = new List<string>();
            var lookup = new List<string>();

            // Fill data
            foreach (var component in components)
            {
                if (component == BindingBase) { continue; }

                var type = component.GetType();
                var propertyAccessors = type.GetProperties(BindingFlags.Static | BindingFlags.Public)
                    .SelectMany(p => p.GetAccessors());

                var eventAccessors = type.GetEvents(BindingFlags.Static | BindingFlags.Public)
                    .SelectMany(e => new[]
                    {
                        e.GetAddMethod(true),
                        e.GetRemoveMethod(true)
                    });

                var accessors = propertyAccessors.Concat(eventAccessors);

                var filter = BindingFlags.Instance | BindingFlags.Public;
                if (declaredMethodsOnly) { filter |= BindingFlags.DeclaredOnly; }

                var methodInfos = component.GetType().GetMethods(filter).Except(accessors);

                foreach (var memberInfo in methodInfos)
                {
                    data.Add(memberInfo);
                    lookup.Add($"{component.GetType()}.{memberInfo.Name}");
                    options.Add($"{component.GetType().PrettyName()}/{memberInfo.Name} - [{memberInfo.Name}]");
                }
            }

            var selection = lookup.IndexOf(viewMethodEntry);

            using var check = new EditorGUI.ChangeCheckScope();
            selection = EditorGUILayout.Popup("Methods", selection, options.ToArray());

            if (BindingEvent == null && selection >= 0) { BindingEvent = data[selection]; }

            if (!check.changed) { return (viewMethodEntry, BindingEvent); }

            BindingEvent = data[selection];
            return (lookup[selection], BindingEvent);
        }

        #endregion

        #region View Event

        protected virtual (string eventEntry, MemberInfo memberInfo) DrawViewEventPopup(string viewEventEntry)
        {
            var components = BindingBase.gameObject.GetComponents<Component>().ToList();
            var data = new List<MemberInfo>();
            var options = new List<string>();
            var lookup = new List<string>();

            // Fill data
            foreach (var component in components)
            {
                if (component == BindingBase) { continue; }

                var members = component.GetType().GetMembers(BindingFlags.Instance | BindingFlags.Public);

                foreach (var memberInfo in members)
                {
                    switch (memberInfo.MemberType)
                    {
                        default:
                            continue;
                        case MemberTypes.Event:
                            break;
                        case MemberTypes.Field:
                            var fieldInfo = memberInfo as FieldInfo;
                            if (!typeof(UnityEventBase).IsAssignableFrom(fieldInfo?.FieldType)) { continue; }

                            break;
                        case MemberTypes.Property:
                            var propertyInfo = memberInfo as PropertyInfo;
                            if (!typeof(UnityEventBase).IsAssignableFrom(propertyInfo?.PropertyType)) { continue; }

                            break;
                    }

                    data.Add(memberInfo);
                    lookup.Add($"{component.GetType()}.{memberInfo.Name}");
                    options.Add($"{component.GetType().PrettyName()}/{memberInfo.Name} - [{memberInfo.Name}]");
                }
            }

            var selection = lookup.IndexOf(viewEventEntry);

            using var check = new EditorGUI.ChangeCheckScope();
            selection = EditorGUILayout.Popup("Events", selection, options.ToArray());

            if (BindingEvent == null && selection >= 0) { BindingEvent = data[selection]; }

            if (!check.changed) { return (viewEventEntry, BindingEvent); }

            BindingEvent = data[selection];
            return (lookup[selection], BindingEvent);
        }

        #endregion

        #region ViewModel Event

        protected string DrawViewModelEventsPopup(string viewModelActionEntry, MemberInfo viewMethodInfo)
        {
            var data = new List<Component>();
            var options = new List<string>();
            var lookup = new List<string>();

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var viewModelTypes = new List<Type>();
            var providerTypeLookup = new Dictionary<Type, IViewModelProvider>();
            var memberInfoLookup = new Dictionary<MemberInfo, IViewModelProvider>();

            var dataProviders = BindingBase.transform.GetComponentsInParent<IViewModelProvider>(true);

            if (dataProviders.Length <= 0)
            {
                var oriCol = EditorStyles.label.normal.textColor;
                EditorStyles.label.normal.textColor = Color.red;

                EditorGUILayout.LabelField("View Model not found in context.");

                EditorStyles.label.normal.textColor = oriCol;
                return null;
            }

            foreach (var dataProvider in dataProviders)
            {
                foreach (var assembly in assemblies)
                {
                    var viewModelType = assembly.GetType(dataProvider.ViewModelTypeName);
                    if (viewModelType == null) { continue; }

                    if (!viewModelTypes.Contains(viewModelType)) { viewModelTypes.Add(viewModelType); }

                    providerTypeLookup[viewModelType] = dataProvider;
                }
            }

            foreach (var viewModelType in viewModelTypes)
            {
                var members = viewModelType.GetMembers(BindingFlags.Instance | BindingFlags.Public);

                foreach (var memberInfo in members)
                {
                    var valueName = "";
                    var viewModelProvider = providerTypeLookup[viewModelType];
                    switch (memberInfo.MemberType)
                    {
                        case MemberTypes.Event:
                            valueName = memberInfo.Name;
                            break;
                        default:
                            continue;
                    }

                    data.Add(viewModelProvider as Component);
                    options.Add($"{viewModelProvider.ViewModelTypeName}/{valueName}");
                    lookup.Add($"{viewModelProvider.ViewModelTypeName}.{valueName}");
                }
            }

            var selection = lookup.IndexOf(viewModelActionEntry);

            using var check = new EditorGUI.ChangeCheckScope();
            selection = EditorGUILayout.Popup("Events", selection, options.ToArray());

            if (selection < 0) { return viewModelActionEntry; }

            if (BindingBase.dataProviderComponent == null) { BindingBase.dataProviderComponent = data[selection]; }

            if (!check.changed) { return viewModelActionEntry; }

            BindingBase.dataProviderComponent = data[selection];
            serializedObject.ApplyModifiedProperties();
            return lookup[selection];
        }

        #endregion

        #region ViewModel Action

        protected string DrawViewModelActionPopup(string viewModelEventEntry, MemberInfo eventMemberInfo)
        {
            var arguments = new List<Type>();
            var options = new List<string>();
            var lookup = new List<string>();

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var viewModelTypes = new List<Type>();
            var providerTypeLookup = new Dictionary<Type, IViewModelProvider>();
            var methodInfoTypeLookup = new Dictionary<MethodInfo, IViewModelProvider>();
            var actionLookup = new Dictionary<int, List<MethodInfo>>
            {
                {0, new List<MethodInfo>()},
                {1, new List<MethodInfo>()},
                {2, new List<MethodInfo>()},
                {3, new List<MethodInfo>()},
                {4, new List<MethodInfo>()},
                {5, new List<MethodInfo>()}
            };

            var providerLookup = new Dictionary<int, List<Component>>
            {
                {0, new List<Component>()},
                {1, new List<Component>()},
                {2, new List<Component>()},
                {3, new List<Component>()},
                {4, new List<Component>()},
                {5, new List<Component>()}
            };

            if (eventMemberInfo == null)
            {
                using (new EditorGUI.DisabledScope(true)) { EditorGUILayout.Popup("Events", -1, options.ToArray()); }

                return viewModelEventEntry;
            }

            switch (eventMemberInfo.MemberType)
            {
                case MemberTypes.Event:
                    var eventInfo = eventMemberInfo as EventInfo;
                    var handlerType = eventInfo?.EventHandlerType;
                    var invokeMethod = handlerType?.GetMethod("Invoke");
                    arguments.AddRange(invokeMethod.GetParameters().Select(p => p.ParameterType));
                    break;
                case MemberTypes.Field:
                    var fieldInfo = eventMemberInfo as FieldInfo;
                    if (!typeof(UnityEventBase).IsAssignableFrom(fieldInfo?.FieldType)) { return null; }

                    arguments.AddRange(EventBinderBase.GetUnityEventType(fieldInfo?.FieldType).GetGenericArguments());

                    break;
                case MemberTypes.Property:
                    var propertyInfo = eventMemberInfo as PropertyInfo;
                    if (!typeof(UnityEventBase).IsAssignableFrom(propertyInfo?.PropertyType)) { return null; }

                    arguments.AddRange(EventBinderBase.GetUnityEventType(propertyInfo?.PropertyType)
                        .GetGenericArguments());

                    break;
                default:
                    return null;
            }

            var dataProviders = BindingBase.transform.GetComponentsInParent<IViewModelProvider>(true);

            if (dataProviders.Length <= 0)
            {
                var oriCol = EditorStyles.label.normal.textColor;
                EditorStyles.label.normal.textColor = Color.red;

                EditorGUILayout.LabelField("View Model not found in context.");

                EditorStyles.label.normal.textColor = oriCol;
                return null;
            }

            foreach (var dataProvider in dataProviders)
            {
                foreach (var assembly in assemblies)
                {
                    var viewModelType = assembly.GetType(dataProvider.ViewModelTypeName);
                    if (viewModelType == null) { continue; }

                    if (!viewModelTypes.Contains(viewModelType)) { viewModelTypes.Add(viewModelType); }

                    providerTypeLookup[viewModelType] = dataProvider;
                }
            }

            foreach (var viewModelType in viewModelTypes)
            {
                var methods = viewModelType
                    .GetMethods(BindingFlags.Instance | BindingFlags.Static |
                                BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => !m.IsSpecialName)
                    .Where(m => !InternalMethodFilter.Contains(m.Name));

                foreach (var methodInfo in methods)
                {
                    var parameters = methodInfo.GetParameters();

                    if (parameters.Length > 5) { continue; }

                    methodInfoTypeLookup[methodInfo] = providerTypeLookup[viewModelType];
                    actionLookup[parameters.Length].Add(methodInfo);
                    providerLookup[arguments.Count].Add(providerTypeLookup[viewModelType] as Component);
                }
            }

            var actionMethods = actionLookup[arguments.Count];
            foreach (var actionMethod in actionMethods)
            {
                var left = actionMethod.GetParameters().Select(p => p.ParameterType);
                var right = arguments;
                if (!left.SequenceEqual(right)) { continue; }

                options.Add($"{methodInfoTypeLookup[actionMethod].ViewModelTypeName}/{actionMethod.Name}");
                lookup.Add($"{methodInfoTypeLookup[actionMethod].ViewModelTypeName}.{actionMethod.Name}");
            }

            var selection = lookup.IndexOf(viewModelEventEntry);

            using var check = new EditorGUI.ChangeCheckScope();
            selection = EditorGUILayout.Popup("Events", selection, options.ToArray());

            if (BindingBase.dataProviderComponent == null && selection >= 0)
            {
                BindingBase.dataProviderComponent = providerLookup[arguments.Count][selection];
            }

            if (!check.changed) { return viewModelEventEntry; }

            BindingBase.dataProviderComponent = providerLookup[arguments.Count][selection];
            serializedObject.ApplyModifiedProperties();
            return lookup[selection];
        }

        #endregion

        #region ViewModel Collection

        protected string DrawViewModelCollectionPopup(string viewModelCollectionEntry)
        {
            var data = new List<Component>();
            var options = new List<string>();
            var lookup = new List<string>();

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var viewModelTypes = new List<Type>();
            var providerTypeLookup = new Dictionary<Type, IViewModelProvider>();
            var memberInfoLookup = new Dictionary<MemberInfo, IViewModelProvider>();

            var dataProviders = BindingBase.transform.GetComponentsInParent<IViewModelProvider>(true);

            if (dataProviders.Length <= 0)
            {
                var oriCol = EditorStyles.label.normal.textColor;
                EditorStyles.label.normal.textColor = Color.red;

                EditorGUILayout.LabelField("View Model not found in context.");

                EditorStyles.label.normal.textColor = oriCol;
                return null;
            }

            foreach (var dataProvider in dataProviders)
            {
                foreach (var assembly in assemblies)
                {
                    var viewModelType = assembly.GetType(dataProvider.ViewModelTypeName);
                    if (viewModelType == null) { continue; }

                    if (!viewModelTypes.Contains(viewModelType)) { viewModelTypes.Add(viewModelType); }

                    providerTypeLookup[viewModelType] = dataProvider;
                }
            }

            foreach (var viewModelType in viewModelTypes)
            {
                var members = viewModelType.GetMembers(BindingFlags.Instance | BindingFlags.Public)
                    .Where(m => m.MemberType == MemberTypes.Property || m.MemberType == MemberTypes.Field);

                foreach (var memberInfo in members)
                {
                    var valueName = "";
                    var viewModelProvider = providerTypeLookup[viewModelType];
                    switch (memberInfo.MemberType)
                    {
                        case MemberTypes.Field:
                            var fieldInfo = (FieldInfo) memberInfo;
                            if (typeof(INotifyCollectionChanged).IsAssignableFrom(fieldInfo.FieldType))
                            {
                                valueName = fieldInfo.Name;
                                memberInfoLookup.Add(fieldInfo, viewModelProvider);
                            }

                            break;
                        case MemberTypes.Property:
                            var propertyInfo = (PropertyInfo) memberInfo;
                            if (typeof(INotifyCollectionChanged).IsAssignableFrom(propertyInfo.PropertyType))
                            {
                                valueName = propertyInfo.Name;
                                memberInfoLookup.Add(propertyInfo, viewModelProvider);
                            }

                            break;
                        default:
                            continue;
                    }

                    data.Add(viewModelProvider as Component);
                    options.Add($"{viewModelProvider.ViewModelTypeName}/{valueName}");
                    lookup.Add($"{viewModelProvider.ViewModelTypeName}.{valueName}");
                }
            }

            var selection = lookup.IndexOf(viewModelCollectionEntry);

            using var check = new EditorGUI.ChangeCheckScope();

            selection = EditorGUILayout.Popup("Collections", selection, options.ToArray());

            if (selection < 0) { return viewModelCollectionEntry; }

            if (BindingBase.dataProviderComponent == null) { BindingBase.dataProviderComponent = data[selection]; }

            if (!check.changed) { return viewModelCollectionEntry; }

            BindingBase.dataProviderComponent = data[selection];
            serializedObject.ApplyModifiedProperties();
            return lookup[selection];
        }

        #endregion

        #region Adatpers

        protected virtual string DrawViewAdapterPopup(string adapterTypeName, SerializedProperty adapterOptionSp)
        {
            (adapterTypeName, ViewModelDest) = DrawAdapterPopup(adapterTypeName, adapterOptionSp
                , ViewModelSource);
            return adapterTypeName;
        }

        protected virtual string DrawViewModelAdapterPopup(string adapterTypeName, SerializedProperty adapterOptionSp)
        {
            (adapterTypeName, ViewDest) = DrawAdapterPopup(adapterTypeName, adapterOptionSp
                , ViewSource);
            return adapterTypeName;
        }

        private (string, Type) DrawAdapterPopup(string adapterTypeName, SerializedProperty adapterOptionSp,
            Type targetType)
        {
            if (targetType == null) { return (adapterTypeName, null); }

            var convertedType = targetType;
            var data = targetType != null && AdapterLookup.ContainsKey(targetType)
                ? AdapterLookup[targetType]
                : new List<Type>();

            var options = data
                .Select(a => $"[{AdapterFromName(a)}]->[{AdapterToName(a)}] : {a.PrettyName()}").ToList();

            options.Insert(0, "None");
            var lookup = data.Select(a => a.FullName).ToList();

            var selection = lookup.IndexOf(adapterTypeName) + 1;
            using var check = new EditorGUI.ChangeCheckScope();

            selection = EditorGUILayout.Popup("Adapters", selection, options.ToArray());
            selection--;

            if (lookup.Count <= selection || selection < 0)
            {
                if (selection < 0) { return (null, null); }

                return (adapterTypeName, null);
            }

            var adapter = data[selection];

            if (AdapterAttributeLookup.TryGetValue(adapter, out var adapterAttribute))
            {
                EditorGUILayout.PropertyField(adapterOptionSp);

                convertedType = adapterAttribute.ToType;
            }

            if (check.changed) { serializedObject.ApplyModifiedProperties(); }

            return (lookup[selection], convertedType);
        }

        #endregion

        #region Collection Handlers

        public string DrawCollectionHandlerPopup(string handlerTypeName)
        {
            var options = ViewCollectionChangedHandlerTypes.Select(a => $"{a.FullPrettyName()}").ToList();

            if (string.IsNullOrEmpty(handlerTypeName) && options.Count > 0) { handlerTypeName = options[0]; }

            var selection = options.IndexOf(handlerTypeName);
            using var check = new EditorGUI.ChangeCheckScope();

            selection = EditorGUILayout.Popup("Handlers", selection, options.ToArray());

            if (check.changed) { serializedObject.ApplyModifiedProperties(); }

            return options[selection];
        }

        #endregion

        #region Info

        protected virtual void DrawBindingLabel(string label, Type type = null)
        {
            using (new EditorGUILayout.HorizontalScope(BindingLabelContainerStyle))
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(120));

                GUILayout.FlexibleSpace();
                if (type != null) { EditorGUILayout.LabelField($"Type: {type.PrettyName()}", AlignToRightStyle); }
            }
        }

        protected virtual void DrawBindingTypeInfo(bool oneWay)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("View Model", GUILayout.Width(70));
                GUILayout.FlexibleSpace();

                var viewModelText = ViewModelSource != null
                    ? ViewModelDest != null
                        ? $"{ViewModelSource.PrettyName()} -> {ViewModelDest.PrettyName()}"
                        : ViewModelSource.PrettyName()
                    : EditorUtil.Undefined;

                var viewText = ViewSource != null
                    ? ViewDest != null
                        ? $"{ViewSource.PrettyName()} -> {ViewDest.PrettyName()}"
                        : ViewSource.PrettyName()
                    : EditorUtil.Undefined;

                var connectionSign = oneWay ? "==>" : "<==>";

                string content;

                if (oneWay)
                {
                    content = ViewModelCurrentType != null && ViewCurrentType != null &&
                              ViewCurrentType.IsAssignableFrom(ViewModelCurrentType)
                        ? $"<color=#006400>{viewModelText} {connectionSign} {viewText}</color>"
                        : $"<color=#640000>{viewModelText} {connectionSign} {viewText}</color>";
                }
                else
                {
                    content = ViewModelSource == ViewCurrentType &&
                              ViewSource == ViewModelCurrentType &&
                              ViewModelCurrentType != null && ViewCurrentType != null
                        ? $"<color=#006400>{viewModelText} {connectionSign} {viewText}</color>"
                        : $"<color=#640000>{viewModelText} {connectionSign} {viewText}</color>";
                }

                var labelContext = new GUIContent(content);
                var labelSize = BindingTypeLabelStyle.CalcSize(labelContext);

                EditorGUILayout.LabelField(labelContext, BindingTypeLabelStyle, GUILayout.Width(labelSize.x));
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("View", GUILayout.Width(30));
            }
        }

        #endregion
    }
}
