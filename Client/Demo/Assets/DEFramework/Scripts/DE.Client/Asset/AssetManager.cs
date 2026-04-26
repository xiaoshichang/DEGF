using Assets.Scripts.DE.Client.Core;
using System;
using System.Threading;
using UnityEngine;

namespace Assets.Scripts.DE.Client.Asset
{
    public sealed class AssetLoadHandle
    {
        private Action<AssetLoadHandle> _completedCallback;
        private AsyncOperation _asyncOperation;
        private readonly AssetProvider _provider;

        internal AssetLoadHandle(
            long handleValue,
            string assetPath,
            Type assetType,
            AssetProvider provider,
            Action<AssetLoadHandle> completedCallback)
        {
            HandleValue = handleValue;
            AssetPath = AssetPathUtility.NormalizeLogicalAssetPath(assetPath);
            AssetType = assetType ?? typeof(UnityEngine.Object);
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _completedCallback = completedCallback;
        }

        public long HandleValue { get; }

        public string AssetPath { get; }

        public Type AssetType { get; }

        public bool IsValid => HandleValue > 0;

        public bool IsDone { get; private set; }

        public bool IsSuccess => IsDone && string.IsNullOrEmpty(Error) && AssetObject != null;

        public bool IsReleased { get; private set; }

        public string Error { get; private set; }

        public UnityEngine.Object AssetObject { get; private set; }

        public float Progress
        {
            get
            {
                if (IsDone)
                {
                    return 1.0f;
                }

                return _asyncOperation == null ? 0.0f : _asyncOperation.progress;
            }
        }

        public T GetAsset<T>() where T : UnityEngine.Object
        {
            return AssetObject as T;
        }

        public void WaitForCompletion()
        {
            AssetManager.WaitForCompletion(this);
        }

        public T WaitForCompletion<T>() where T : UnityEngine.Object
        {
            WaitForCompletion();
            return GetAsset<T>();
        }

        public void Release()
        {
            AssetManager.Release(this);
        }

        public void RegisterCompleted(Action<AssetLoadHandle> completedCallback)
        {
            if (completedCallback == null)
            {
                return;
            }

            var invokeImmediately = false;
            if (IsDone)
            {
                invokeImmediately = true;
            }
            else
            {
                _completedCallback += completedCallback;
            }

            if (invokeImmediately)
            {
                _InvokeCallback(completedCallback);
            }
        }

        internal void SetAsyncOperation(AsyncOperation asyncOperation)
        {
            _asyncOperation = asyncOperation;
        }

        internal void Complete(UnityEngine.Object assetObject, string error)
        {
            if (IsDone || IsReleased)
            {
                return;
            }

            IsDone = true;
            AssetObject = assetObject;
            Error = error ?? string.Empty;
            _asyncOperation = null;
            var completedCallback = _completedCallback;
            _completedCallback = null;

            if (completedCallback != null)
            {
                _InvokeCallback(completedCallback);
            }
        }

        internal AssetProvider GetProvider()
        {
            return _provider;
        }

        internal void MarkReleased()
        {
            if (IsReleased)
            {
                return;
            }

            IsReleased = true;
            _asyncOperation = null;
            _completedCallback = null;
            AssetObject = null;
            Error = string.Empty;
        }

        private void _InvokeCallback(Action<AssetLoadHandle> completedCallback)
        {
            try
            {
                completedCallback(this);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }

    public static class AssetManager
    {
        private static long s_nextHandleValue;
        private static bool s_isInitialized;
        private static AssetProviderMode s_providerMode;
        private static AssetProvider s_provider;

        private const string ProviderModePreferenceKey = "DE.Client.Asset.ProviderMode";

        public static AssetProviderMode CurrentProviderMode
        {
            get
            {
                if (s_isInitialized)
                {
                    return s_providerMode;
                }

                return GetConfiguredProviderMode();
            }
        }

        public static void Init()
        {
            if (s_isInitialized)
            {
                return;
            }

            s_providerMode = GetConfiguredProviderMode();
            s_provider = _CreateProvider(s_providerMode);
            s_provider.Init();
            s_isInitialized = true;

            DELogger.Info("AssetManager", "Initialized with provider mode: " + s_providerMode + ".");
        }

        public static void UnInit()
        {
            if (!s_isInitialized)
            {
                return;
            }

            var providerToUninit = s_provider;
            s_provider = null;
            s_isInitialized = false;

            if (providerToUninit != null)
            {
                providerToUninit.UnInit();
            }
        }

        public static AssetLoadHandle LoadAssetAsync<T>(
            string assetPath,
            Action<AssetLoadHandle> completedCallback = null) where T : UnityEngine.Object
        {
            return LoadAssetAsync(assetPath, typeof(T), completedCallback);
        }

        public static AssetLoadHandle LoadAssetAsync<T>(
            string assetPath,
            Action<T> onLoaded,
            Action<string> onFailed = null) where T : UnityEngine.Object
        {
            return LoadAssetAsync<T>(
                assetPath,
                handle =>
                {
                    if (handle.IsSuccess)
                    {
                        onLoaded?.Invoke(handle.GetAsset<T>());
                        return;
                    }

                    onFailed?.Invoke(handle.Error);
                });
        }

        public static AssetLoadHandle LoadAssetAsync(
            string assetPath,
            Type assetType,
            Action<AssetLoadHandle> completedCallback = null)
        {
            if (assetType == null)
            {
                throw new ArgumentNullException(nameof(assetType));
            }

            if (!typeof(UnityEngine.Object).IsAssignableFrom(assetType))
            {
                throw new ArgumentException("Asset type must inherit from UnityEngine.Object.", nameof(assetType));
            }

            var normalizedAssetPath = AssetPathUtility.NormalizeLogicalAssetPath(assetPath);
            if (string.IsNullOrWhiteSpace(normalizedAssetPath))
            {
                throw new ArgumentException("Asset path is required.", nameof(assetPath));
            }

            _EnsureInitialized();

            var provider = s_provider;

            var handle = new AssetLoadHandle(
                Interlocked.Increment(ref s_nextHandleValue),
                normalizedAssetPath,
                assetType,
                provider,
                completedCallback);

            try
            {
                provider.LoadAsync(handle);
            }
            catch (Exception exception)
            {
                var error = "Load asset async failed, path=" + normalizedAssetPath + ", error=" + exception.Message;
                DELogger.Error("AssetManager", error);
                handle.Complete(null, error);
            }

            return handle;
        }

        public static T LoadAsset<T>(string assetPath) where T : UnityEngine.Object
        {
            var handle = LoadAssetAsync<T>(assetPath);
            return handle.WaitForCompletion<T>();
        }

        public static void Release(AssetLoadHandle handle)
        {
            if (handle == null || !handle.IsValid || handle.IsReleased)
            {
                return;
            }

            handle.GetProvider().Release(handle);
            handle.MarkReleased();
        }

        public static AssetProviderMode GetConfiguredProviderMode()
        {
#if UNITY_EDITOR
            return (AssetProviderMode)PlayerPrefs.GetInt(
                ProviderModePreferenceKey,
                (int)AssetProviderMode.AssetDatabase);
#else
            return AssetProviderMode.AssetBundle;
#endif
        }

        public static void SetProviderMode(AssetProviderMode providerMode)
        {
            PlayerPrefs.SetInt(ProviderModePreferenceKey, (int)providerMode);
            PlayerPrefs.Save();

            AssetProvider currentProvider = null;
            AssetProvider nextProvider = null;
            var shouldSwitchProvider = false;

            if (!s_isInitialized)
            {
                s_providerMode = providerMode;
                return;
            }

            if (s_providerMode == providerMode)
            {
                return;
            }

            currentProvider = s_provider;
            nextProvider = _CreateProvider(providerMode);
            s_provider = nextProvider;
            s_providerMode = providerMode;
            shouldSwitchProvider = true;

            if (!shouldSwitchProvider)
            {
                return;
            }

            if (currentProvider != null)
            {
                currentProvider.UnInit();
            }

            nextProvider.Init();
            DELogger.Info("AssetManager", "Provider switched to: " + providerMode + ".");
        }

        internal static void WaitForCompletion(AssetLoadHandle handle)
        {
            if (handle == null)
            {
                throw new ArgumentNullException(nameof(handle));
            }

            if (!handle.IsValid)
            {
                throw new ArgumentException("Asset load handle is invalid.", nameof(handle));
            }

            if (handle.IsDone)
            {
                return;
            }

            _EnsureInitialized();
            handle.GetProvider().WaitForCompletion(handle);
        }

        private static void _EnsureInitialized()
        {
            if (s_isInitialized)
            {
                return;
            }

            Init();
        }

        private static AssetProvider _CreateProvider(AssetProviderMode providerMode)
        {
            switch (providerMode)
            {
                case AssetProviderMode.AssetDatabase:
                    return new AssetDBAssetProvider();
                case AssetProviderMode.AssetBundle:
                    return new AssetBundleAssetProvider();
                default:
                    throw new ArgumentOutOfRangeException(nameof(providerMode), providerMode, null);
            }
        }
    }
}
