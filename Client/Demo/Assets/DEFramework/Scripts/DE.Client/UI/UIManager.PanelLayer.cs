using System;
using System.Collections.Generic;
using System.Reflection;
using Assets.Scripts.DE.Client.Asset;
using Assets.Scripts.DE.Client.Core;
using UnityEngine;

namespace Assets.Scripts.DE.Client.UI
{
    public partial class UIManager
    {
        private sealed class PanelMetadata
        {
            public Type PanelType;
            public string PrefabPath;
        }

        private sealed class PanelStackEntry
        {
            public PanelBase Panel;
            public object ShowArg;
        }

        private readonly Dictionary<Type, PanelMetadata> _PanelMetadataByType = new Dictionary<Type, PanelMetadata>();
        private readonly Dictionary<Type, AssetLoadHandle> _PanelPrefabHandleByType = new Dictionary<Type, AssetLoadHandle>();
        private readonly Stack<PanelStackEntry> _PanelStack = new Stack<PanelStackEntry>();

        public int PanelStackCount => _PanelStack.Count;

        public T PushPanel<T>(object arg = null) where T : PanelBase
        {
            return PushPanel(typeof(T), arg) as T;
        }

        public PanelBase PushPanel(Type panelType, object arg = null)
        {
            if (panelType == null)
            {
                throw new ArgumentNullException(nameof(panelType));
            }

            if (!typeof(PanelBase).IsAssignableFrom(panelType))
            {
                throw new ArgumentException("panelType must inherit from PanelBase.", nameof(panelType));
            }

            var metadata = _GetPanelMetadata(panelType);
            if (metadata == null)
            {
                DELogger.Error("UIManager", "PushPanel failed because panel metadata is missing, panelType=" + panelType.FullName + ".");
                return null;
            }

            var panel = _CreatePanelInstance(metadata);
            if (panel == null)
            {
                return null;
            }

            var previousTopEntry = _PanelStack.Count > 0 ? _PanelStack.Peek() : null;
            if (previousTopEntry != null)
            {
                previousTopEntry.Panel.HidePanel();
            }

            var entry = new PanelStackEntry();
            entry.Panel = panel;
            entry.ShowArg = arg;
            _PanelStack.Push(entry);
            panel.ShowPanel(arg);
            return panel;
        }

        public PanelBase PopPanel()
        {
            if (_PanelStack.Count == 0)
            {
                DELogger.Warn("UIManager", "PopPanel ignored because panel stack is empty.");
                return null;
            }

            var topEntry = _PanelStack.Pop();
            topEntry.Panel.HidePanel();
            UnityEngine.Object.Destroy(topEntry.Panel.gameObject);

            if (_PanelStack.Count > 0)
            {
                var nextTopEntry = _PanelStack.Peek();
                nextTopEntry.Panel.ShowPanel(nextTopEntry.ShowArg);
            }

            return topEntry.Panel;
        }

        public PanelBase PeekPanel()
        {
            if (_PanelStack.Count == 0)
            {
                return null;
            }

            return _PanelStack.Peek().Panel;
        }

        private void _InitPanelLayer()
        {
            _CollectPanelMetadata();
        }

        private void _UninitPanelLayer()
        {
            while (_PanelStack.Count > 0)
            {
                var entry = _PanelStack.Pop();
                if (entry.Panel != null)
                {
                    entry.Panel.HidePanel();
                    UnityEngine.Object.Destroy(entry.Panel.gameObject);
                }
            }

            foreach (var handle in _PanelPrefabHandleByType.Values)
            {
                if (handle != null)
                {
                    handle.Release();
                }
            }

            _PanelPrefabHandleByType.Clear();
            _PanelMetadataByType.Clear();
        }

        private void _CollectPanelMetadata()
        {
            _PanelMetadataByType.Clear();

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                foreach (var type in _GetAssemblyTypes(assembly))
                {
                    if (type == null || type.IsAbstract || type.IsGenericTypeDefinition)
                    {
                        continue;
                    }

                    if (!typeof(PanelBase).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    var attribute = type.GetCustomAttribute<PanelAttribute>(false);
                    if (attribute == null)
                    {
                        DELogger.Warn("UIManager", "Ignore panel without PanelAttribute, panelType=" + type.FullName + ".");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(attribute.PrefabPath))
                    {
                        DELogger.Warn("UIManager", "Ignore panel with empty prefab path, panelType=" + type.FullName + ".");
                        continue;
                    }

                    var metadata = new PanelMetadata();
                    metadata.PanelType = type;
                    metadata.PrefabPath = attribute.PrefabPath;
                    _PanelMetadataByType[type] = metadata;
                }
            }

            DELogger.Info("UIManager", _PanelMetadataByType.Count + " panel metadata item(s) collected.");
        }

        private IEnumerable<Type> _GetAssemblyTypes(Assembly assembly)
        {
            if (assembly == null)
            {
                yield break;
            }

            Type[] types = null;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types;
                DELogger.Warn("UIManager", "Load types from assembly '" + assembly.FullName + "' partially failed: " + exception.Message);
            }

            if (types == null)
            {
                yield break;
            }

            foreach (var type in types)
            {
                if (type != null)
                {
                    yield return type;
                }
            }
        }

        private PanelMetadata _GetPanelMetadata(Type panelType)
        {
            PanelMetadata metadata;
            if (_PanelMetadataByType.TryGetValue(panelType, out metadata))
            {
                return metadata;
            }

            return null;
        }

        private PanelBase _CreatePanelInstance(PanelMetadata metadata)
        {
            if (metadata == null)
            {
                return null;
            }

            if (_PanelLayerNode == null)
            {
                DELogger.Error("UIManager", "PanelLayerNode is not initialized.");
                return null;
            }

            var panelPrefab = _LoadPanelPrefab(metadata);
            if (panelPrefab == null)
            {
                return null;
            }

            var panelNode = UnityEngine.Object.Instantiate(panelPrefab, _PanelLayerNode.transform, false);
            panelNode.name = metadata.PanelType.Name;
            panelNode.SetActive(false);

            var rectTransform = panelNode.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                _StretchToFullScreen(rectTransform);
            }

            var panel = panelNode.GetComponent<PanelBase>();
            if (panel == null)
            {
                DELogger.Error("UIManager", "Panel prefab does not contain PanelBase, panelType=" + metadata.PanelType.FullName + ", prefabPath=" + metadata.PrefabPath + ".");
                UnityEngine.Object.Destroy(panelNode);
                return null;
            }

            if (!metadata.PanelType.IsInstanceOfType(panel))
            {
                DELogger.Error("UIManager", "Panel prefab type mismatch, expected=" + metadata.PanelType.FullName + ", actual=" + panel.GetType().FullName + ", prefabPath=" + metadata.PrefabPath + ".");
                UnityEngine.Object.Destroy(panelNode);
                return null;
            }

            return panel;
        }

        private GameObject _LoadPanelPrefab(PanelMetadata metadata)
        {
            AssetLoadHandle handle;
            if (!_PanelPrefabHandleByType.TryGetValue(metadata.PanelType, out handle))
            {
                if (AssetManager.Instance == null)
                {
                    DELogger.Error("UIManager", "AssetManager is not initialized.");
                    return null;
                }

                handle = AssetManager.Instance.LoadAssetAsync<GameObject>(metadata.PrefabPath);
                handle.WaitForCompletion();
                _PanelPrefabHandleByType[metadata.PanelType] = handle;
            }

            if (!handle.IsSuccess)
            {
                DELogger.Error("UIManager", "Load panel prefab failed, panelType=" + metadata.PanelType.FullName + ", prefabPath=" + metadata.PrefabPath + ", error=" + handle.Error);
                return null;
            }

            var panelPrefab = handle.GetAsset<GameObject>();
            if (panelPrefab == null)
            {
                DELogger.Error("UIManager", "Panel prefab asset is null, panelType=" + metadata.PanelType.FullName + ", prefabPath=" + metadata.PrefabPath + ".");
                return null;
            }

            return panelPrefab;
        }
    }
}
