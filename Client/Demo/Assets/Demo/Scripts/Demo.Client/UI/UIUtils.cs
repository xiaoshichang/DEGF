
using UnityEngine;

namespace Assets.Scripts.Demo.Client.UI
{
    public static class UIUtils
    {

        public static T FindChildComponent<T>(Transform parentNode, string childName) where T : Component
        {
            return _FindChildComponent<T>(parentNode, childName);
        }

        private static T _FindChildComponent<T>(Transform parentNode, string childName) where T : Component
        {
            var childNode = _FindChildRecursive(parentNode, childName);
            return childNode == null ? null : childNode.GetComponent<T>();
        }

        private static Transform _FindChildRecursive(Transform parentNode, string childName)
        {
            if (parentNode == null || string.IsNullOrEmpty(childName))
            {
                return null;
            }

            if (parentNode.name == childName)
            {
                return parentNode;
            }

            for (int index = 0; index < parentNode.childCount; index++)
            {
                var childNode = _FindChildRecursive(parentNode.GetChild(index), childName);
                if (childNode != null)
                {
                    return childNode;
                }
            }

            return null;
        }
    }


}