using System;
using System.IO;
using UnityEngine;

namespace Assets.Scripts.DE.Client.Asset
{
    public enum AssetProviderMode
    {
        AssetDatabase = 0,
        AssetBundle = 1,
    }

    public abstract class AssetProvider
    {
        public abstract AssetProviderMode Mode { get; }

        public virtual void Init()
        {
        }

        public virtual void UnInit()
        {
        }

        public abstract void LoadAsync(AssetLoadHandle handle);

        public abstract void WaitForCompletion(AssetLoadHandle handle);

        public virtual void Release(AssetLoadHandle handle)
        {
        }
    }

    public static class AssetPathUtility
    {
        public const string AssetBundleName = "degf_client_assets";
        public const string AssetBundleRootDirectoryName = "Asb";
        private const string AssetDatabaseRootPrefix = "Assets/";
        private const string AssetBundleRootMarker = "/Asb/";

        public static string NormalizeLogicalAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return string.Empty;
            }

            var normalizedPath = assetPath.Replace('\\', '/').Trim();
            if (normalizedPath.StartsWith("./", StringComparison.Ordinal))
            {
                normalizedPath = normalizedPath.Substring(2);
            }

            if (normalizedPath.StartsWith(AssetDatabaseRootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath = normalizedPath.Substring(AssetDatabaseRootPrefix.Length);
            }

            return normalizedPath;
        }

        public static string ToAssetDatabasePath(string logicalAssetPath)
        {
            var normalizedLogicalPath = NormalizeLogicalAssetPath(logicalAssetPath);
            if (string.IsNullOrEmpty(normalizedLogicalPath))
            {
                return string.Empty;
            }

            return AssetDatabaseRootPrefix + normalizedLogicalPath;
        }

        public static bool IsBundleCandidatePath(string assetDatabasePath)
        {
            if (string.IsNullOrWhiteSpace(assetDatabasePath))
            {
                return false;
            }

            var normalizedPath = assetDatabasePath.Replace('\\', '/');
            if (!normalizedPath.StartsWith(AssetDatabaseRootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return normalizedPath.IndexOf(AssetBundleRootMarker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string GetStreamingAssetBundleRootDirectory()
        {
            return Path.Combine(Application.streamingAssetsPath, AssetBundleRootDirectoryName);
        }

        public static string GetStreamingAssetBundlePlatformDirectory()
        {
            return Path.Combine(GetStreamingAssetBundleRootDirectory(), GetRuntimePlatformDirectoryName());
        }

        public static string GetStreamingAssetBundlePath()
        {
            return Path.Combine(GetStreamingAssetBundlePlatformDirectory(), AssetBundleName);
        }

        public static string GetRuntimePlatformDirectoryName()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.WindowsPlayer:
                    return "Windows";
                case RuntimePlatform.IPhonePlayer:
                    return "iOS";
                default:
                    return "Windows";
            }
        }
    }
}
