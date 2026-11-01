using Assets.Scripts.DE.Client.Core;
using System;
using System.IO;
using UnityEngine;

namespace Assets.Scripts.DE.Client.Asset
{
    public sealed class AssetBundleAssetProvider : AssetProvider
    {
        private AssetBundle _assetBundle;
        private string _bundleLoadError;

        public override AssetProviderMode Mode => AssetProviderMode.AssetBundle;

        public override void Init()
        {
            base.Init();

            if (_assetBundle != null)
            {
                return;
            }

            var bundlePath = AssetPathUtility.GetStreamingAssetBundlePath();
            if (!File.Exists(bundlePath))
            {
                _bundleLoadError = "AssetBundle file does not exist: " + bundlePath + ".";
                DELogger.Error("AssetManager", _bundleLoadError);
                return;
            }

            _assetBundle = AssetBundle.LoadFromFile(bundlePath);
            if (_assetBundle == null)
            {
                _bundleLoadError = "AssetBundle load failed: " + bundlePath + ".";
                DELogger.Error("AssetManager", _bundleLoadError);
                return;
            }

            _bundleLoadError = string.Empty;
        }

        public override void UnInit()
        {
            if (_assetBundle != null)
            {
                _assetBundle.Unload(false);
                _assetBundle = null;
            }

            _bundleLoadError = string.Empty;
            base.UnInit();
        }

        public override void LoadAsync(AssetLoadHandle handle)
        {
            if (handle == null)
            {
                throw new ArgumentNullException(nameof(handle));
            }

            if (_assetBundle == null)
            {
                handle.Complete(null, _ResolveBundleLoadError());
                return;
            }

            var request = _assetBundle.LoadAssetAsync(handle.AssetPath, handle.AssetType);
            handle.SetAsyncOperation(request);
            request.completed += _ => _CompleteRequest(handle, request);
        }

        public override void WaitForCompletion(AssetLoadHandle handle)
        {
            if (handle == null || handle.IsDone)
            {
                return;
            }

            if (_assetBundle == null)
            {
                handle.Complete(null, _ResolveBundleLoadError());
                return;
            }

            var assetObject = _assetBundle.LoadAsset(handle.AssetPath, handle.AssetType);
            if (assetObject == null)
            {
                handle.Complete(
                    null,
                    "AssetBundle sync load failed, path=" + handle.AssetPath + ", type=" + handle.AssetType.Name + ".");
                return;
            }

            handle.Complete(assetObject, string.Empty);
        }

        public override void Release(AssetLoadHandle handle)
        {
            if (handle == null)
            {
                return;
            }
        }

        private void _CompleteRequest(AssetLoadHandle handle, AssetBundleRequest request)
        {
            if (handle.IsDone)
            {
                return;
            }

            if (request == null || request.asset == null)
            {
                handle.Complete(
                    null,
                    "AssetBundle async load failed, path=" + handle.AssetPath + ", type=" + handle.AssetType.Name + ".");
                return;
            }

            handle.Complete(request.asset, string.Empty);
        }

        private string _ResolveBundleLoadError()
        {
            if (!string.IsNullOrWhiteSpace(_bundleLoadError))
            {
                return _bundleLoadError;
            }

            var bundlePath = AssetPathUtility.GetStreamingAssetBundlePath();
            return "AssetBundle provider is unavailable, bundle path=" + bundlePath + ".";
        }
    }
}
