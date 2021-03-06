using System;
using System.Reflection;
using Hermit.Views;
using UnityEngine;
using UnityEngine.UI;
using Component = UnityEngine.Component;

namespace Hermit.DataBinding
{
    [ScriptOrder(-2000)]
    public abstract class DataBindingBase : MonoBehaviour
    {
        public Component dataProviderComponent;

        #region Runtime Variables

        protected ViewModel ViewModel;

        protected bool IsDataReady;

        protected bool IsBindingConnected;

        protected IViewModelProvider DataProvider;

        #endregion

        #region Helpers

        protected virtual void Awake()
        {
            if (dataProviderComponent != null && dataProviderComponent is IViewModelProvider provider)
            {
                DataProvider = provider;

                if (DataProvider.GetViewModel() != null)
                {
                    IsDataReady = true;
                    SetupBinding();
                }
                else { DataProvider.OnDataReady += OnDataReady; }
            }
            else
            {
                DataProvider = GetComponentInParent<IViewModelProvider>();

                if (DataProvider.GetViewModel() != null)
                {
                    IsDataReady = true;
                    SetupBinding();
                }
                else { DataProvider.OnDataReady += OnDataReady; }
            }

            DataProvider?.DataBindings.Add(this);
        }

        protected virtual void OnDestroy()
        {
            if (DataProvider != null) { DataProvider.OnDataReady -= OnDataReady; }

            DataProvider?.DataBindings.Remove(this);
        }

        protected virtual void OnEnable()
        {
            if (!IsDataReady) { return; }

            if (IsBindingConnected) { return; }

            Connect();
        }

        protected virtual void OnDisable()
        {
            if (!IsDataReady) { return; }

            if (!IsBindingConnected) { return; }

            Disconnect();
        }

        private void OnDataReady()
        {
            if (!IsDataReady)
            {
                IsDataReady = true;
                SetupBinding();
            }

            if (!enabled || IsBindingConnected) { return; }

            Connect();
        }

        public virtual void SetupBinding() { ViewModel = DataProvider.GetViewModel(); }

        public virtual void Connect() { IsBindingConnected = true; }

        public virtual void Disconnect() { IsBindingConnected = false; }

        public abstract void UpdateBinding();

        protected (string typeName, string memberName) ParseEntry2TypeMember(string entry)
        {
            var lastPeriodIndex = entry.LastIndexOf('.');
            if (lastPeriodIndex == -1) { throw new Exception($"No period was found[{entry}] on {name}"); }

            var typeName = entry.Substring(0, lastPeriodIndex);
            var memberName = entry.Substring(lastPeriodIndex + 1);

            //Due to (undocumented) unity behaviour, some of their components do not work with the namespace when using GetComponent(""), and all of them work without the namespace
            //So to be safe, we remove all namespaces from any component that starts with UnityEngine
            if (typeName.StartsWith("UnityEngine.")) { typeName = typeName.Substring(typeName.LastIndexOf('.') + 1); }

            if (typeName.Length == 0 || memberName.Length == 0)
            {
                App.Error($"Bad formatting! Expected [<type-name>.<member-name>]: {entry} ");
                return (null, null);
            }

            return (typeName, memberName);
        }

        protected MemberInfo ParseViewModelEntry(ViewModel viewModel, string entry)
        {
            var (_, memberName) = ParseEntry2TypeMember(entry);

            var viewMemberInfos = viewModel.GetType().GetMember(memberName);
            if (viewMemberInfos.Length <= 0)
            {
                App.Error($"Can't find member of name: {memberName} on {viewModel}.");
                return null;
            }

            var memberInfo = viewMemberInfos[0];
            return memberInfo;
        }

        protected (Component component, MemberInfo memberInfo) ParseViewEntry(Component viewProvider,
            string entry)
        {
            var (typeName, memberName) = ParseEntry2TypeMember(entry);

            var type = AssemblyHelper.GetTypeInAppDomain(typeName);
            var components = viewProvider.GetComponents(type);
            if (components == null)
            {
                App.Error($"Can't find component of type: {typeName} on {viewProvider}.");
                return (null, null);
            }

            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];

                var viewMemberInfos = component.GetType().GetMember(memberName);
                if (viewMemberInfos.Length <= 0)
                {
                    App.Error($"Can't find member of name: {memberName} on {component}.");
                    continue;
                }

                var memberInfo = viewMemberInfos[0];
                return (component, memberInfo);
            }

            return (null, null);
        }

        #endregion
    }
}
