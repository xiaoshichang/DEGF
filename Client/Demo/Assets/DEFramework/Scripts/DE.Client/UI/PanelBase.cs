using System;
using UnityEngine;

namespace Assets.Scripts.DE.Client.UI
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class PanelAttribute : Attribute
    {
        public PanelAttribute(string prefabPath)
        {
            PrefabPath = prefabPath;
        }

        public string PrefabPath { get; }
    }

    public abstract class PanelBase : MonoBehaviour
    {
        private bool _IsShowing;

        private void Awake()
        {
            Bind();
        }

        /// <summary>
        /// bind UI elements to fields and events. 
        /// This method is called in Awake, which means it will be called before Start and before the panel is shown. 
        /// You can use this method to initialize references to UI components and set up event listeners.
        /// </summary>
        protected abstract void Bind();

        /// <summary>
        /// When implemented in a derived class, defines the behavior when the panel is shown. 
        /// The argument can be used to pass data to the panel when it is shown, such as parameters for initializing the panel's content or state.
        /// </summary>
        /// <param name="arg">The argument passed to the panel when it is shown.</param>
        protected abstract void OnShow(object arg);

        /// <summary>
        /// When implemented in a derived class, defines the behavior when the panel is hidden.
        /// </summary>
        protected abstract void OnHide();

        internal void ShowPanel(object arg)
        {
            if (_IsShowing)
            {
                OnShow(arg);
                return;
            }

            _IsShowing = true;
            gameObject.SetActive(true);
            OnShow(arg);
        }

        internal void HidePanel()
        {
            if (!_IsShowing)
            {
                return;
            }

            _IsShowing = false;
            OnHide();
            gameObject.SetActive(false);
        }
    }
}
