using Assets.Scripts.DE.Client.Core;
using System;
using UnityEngine;

namespace Assets.Scripts.DE.Client.Asset
{
    public sealed class AssetLoadHandle
    {
        private Action<AssetLoadHandle> _completedCallback;
        private AsyncOperation _asyncOperation;
        private readonly AssetManager _assetManager;
        private readonly AssetProvider _provider;

        internal AssetLoadHandle(
            long handleValue,
            string assetPath,
            Type assetType,
            AssetManager assetManager,
            AssetProvider provider,
            Action<AssetLoadHandle> completedCallback)
        {
            HandleValue = handleValue;
            AssetPath = AssetPathUtility.NormalizeLogicalAssetPath(assetPath);
            AssetType = assetType ?? typeof(UnityEngine.Object);
            _assetManager = assetManager ?? throw new ArgumentNullException(nameof(assetManager));
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
            _assetManager.WaitForCompletion(this);
        }

        public T WaitForCompletion<T>() where T : UnityEngine.Object
        {
            WaitForCompletion();
            return GetAsset<T>();
        }

        public void Release()
        {
            _assetManager.Release(this);
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

    public sealed class AssetManager
    {
        private const string ProviderModePreferenceKey = "DE.Client.Asset.ProviderMode";

        public static AssetManager Instance;

        private long _nextHandleValue;
        private bool _isInitialized;
        private AssetProviderMode _providerMode;
        private AssetProvider _provider;

        public AssetProviderMode CurrentProviderMode
        {
            get
            {
                if (_isInitialized)
                {
                    return _providerMode;
                }

                return GetConfiguredProviderMode();
            }
        }

        public void Init()
        {
            if (_isInitialized)
            {
                return;
            }

            _providerMode = GetConfiguredProviderMode();
            _provider = _CreateProvider(_providerMode);
            _provider.Init();
            _isInitialized = true;

            DELogger.Info("AssetManager", "Initialized with provider mode: " + _providerMode + ".");
        }

        public void UnInit()
        {
            if (!_isInitialized)
            {
                return;
            }

            var providerToUninit = _provider;
            _provider = null;
            _isInitialized = false;

            if (providerToUninit != null)
            {
                providerToUninit.UnInit();
            }
        }

        public AssetLoadHandle LoadAssetAsync<T>(
            string assetPath,
            Action<AssetLoadHandle> completedCallback = null) where T : UnityEngine.Object
        {
            return LoadAssetAsync(assetPath, typeof(T), completedCallback);
        }

        public AssetLoadHandle LoadAssetAsync<T>(
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

        public AssetLoadHandle LoadAssetAsync(
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

            var provider = _provider;
            var handle = new AssetLoadHandle(
                ++_nextHandleValue,
                normalizedAssetPath,
                assetType,
                this,
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

        public T LoadAsset<T>(string assetPath) where T : UnityEngine.Object
        {
            var handle = LoadAssetAsync<T>(assetPath);
            return handle.WaitForCompletion<T>();
        }

        public void Release(AssetLoadHandle handle)
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

        public void SetProviderMode(AssetProviderMode providerMode)
        {
            SetConfiguredProviderMode(providerMode);

            if (!_isInitialized)
            {
                _providerMode = providerMode;
                return;
            }

            if (_providerMode == providerMode)
            {
                return;
            }

            var currentProvider = _provider;
            var nextProvider = _CreateProvider(providerMode);
            _provider = nextProvider;
            _providerMode = providerMode;

            if (currentProvider != null)
            {
                currentProvider.UnInit();
            }

            nextProvider.Init();
            DELogger.Info("AssetManager", "Provider switched to: " + providerMode + ".");
        }

        public static void SetConfiguredProviderMode(AssetProviderMode providerMode)
        {
            PlayerPrefs.SetInt(ProviderModePreferenceKey, (int)providerMode);
            PlayerPrefs.Save();
        }

        internal void WaitForCompletion(AssetLoadHandle handle)
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

        private void _EnsureInitialized()
        {
            if (_isInitialized)
            {
                return;
            }

            Init();
        }

        private AssetProvider _CreateProvider(AssetProviderMode providerMode)
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
