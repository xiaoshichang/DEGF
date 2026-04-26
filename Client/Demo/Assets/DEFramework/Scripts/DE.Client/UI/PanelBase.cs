using System;
using Assets.Scripts.DE.Client.Asset;
using UnityEngine;

namespace Assets.Scripts.DE.Client.UI
{
    public abstract class PanelBase : MonoBehaviour
    {
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

        /// <summary>
        /// When implemented in a derived class, gets the file system path to the associated prefab resource.
        /// </summary>
        protected abstract string GetPrefabPath();

    }
}
