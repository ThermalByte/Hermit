using System;
using System.Collections.Generic;
using Hermit.DataBinding;
using UnityEngine;

namespace Hermit.Views
{
    public abstract class ViewBase<TViewModel> : MonoBehaviour, IViewModelProvider, IView where TViewModel : ViewModel
    {
        protected IViewManager ViewManager { get; private set; }

        public List<DataBindingBase> DataBindings { get; } = new List<DataBindingBase>();

        #region IView

        public ulong ViewId { get; private set; }

        public Component component => this;

        /// <summary>
        /// Should be call manually.
        /// </summary>
        public virtual void SetUpViewInfo()
        {
            ViewManager = App.Resolve<IViewManager>();
            ViewId = ViewManager.Register(this);
        }

        /// <summary>
        /// Should be call manually.
        /// </summary>
        public virtual void CleanUpViewInfo()
        {
            ViewManager.UnRegister(ViewId);
            if (DataContext != null && !DataContext.Reusable) { DataContext?.Dispose(); }
        }

        #endregion

        #region IViewModelProvider

        public TViewModel DataContext { get; protected set; }

        public void SetViewModel(TViewModel dataContext)
        {
            DataContext = dataContext;
            OnDataReady?.Invoke();
        }

        public void SetViewModel(object context)
        {
            if (context is TViewModel viewModel) { DataContext = viewModel; }
            else { App.Warn($"{context} is not matching {typeof(TViewModel)}"); }

            OnDataReady?.Invoke();
            ReBindAll();
        }

        public virtual TViewModel GetViewModel() => DataContext;

        public string ViewModelTypeName => typeof(TViewModel).FullName;

        public void ReBindAll()
        {
            if (DataBindings != null)
            {
                foreach (var db in DataBindings)
                {
                    db.Disconnect();
                    db.SetupBinding();
                    db.Connect();
                }
            }
        }

        public event Action OnDataReady;

        ViewModel IViewModelProvider.GetViewModel()
        {
            return GetViewModel();
        }

        #endregion
    }
}