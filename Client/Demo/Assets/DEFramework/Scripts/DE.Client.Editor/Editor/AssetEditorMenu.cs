using Assets.Scripts.DE.Client.Asset;
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Assets.Scripts.DE.Client.Editor.Asset
{
    public static class AssetEditorMenu
    {
        private const string RootMenu = "DEGF/Asset/";
        private const string BuildWindowsMenu = RootMenu + "Build AssetBundle/Windows";
        private const string BuildIosMenu = RootMenu + "Build AssetBundle/iOS";
        private const string UseAssetDatabaseMenu = RootMenu + "Provider/Use AssetDatabase";
        private const string UseAssetBundleMenu = RootMenu + "Provider/Use AssetBundle";

        [MenuItem(BuildWindowsMenu)]
        public static void BuildWindowsAssetBundle()
        {
            BuildAssetBundleForTarget(BuildTarget.StandaloneWindows64, "Windows");
        }

        [MenuItem(BuildIosMenu)]
        public static void BuildIosAssetBundle()
        {
            BuildAssetBundleForTarget(BuildTarget.iOS, "iOS");
        }

        [MenuItem(UseAssetDatabaseMenu)]
        public static void UseAssetDatabaseProvider()
        {
            AssetManager.SetConfiguredProviderMode(AssetProviderMode.AssetDatabase);
            if (AssetManager.Instance != null)
            {
                AssetManager.Instance.SetProviderMode(AssetProviderMode.AssetDatabase);
            }

            _RefreshProviderMenuState();
        }

        [MenuItem(UseAssetBundleMenu)]
        public static void UseAssetBundleProvider()
        {
            AssetManager.SetConfiguredProviderMode(AssetProviderMode.AssetBundle);
            if (AssetManager.Instance != null)
            {
                AssetManager.Instance.SetProviderMode(AssetProviderMode.AssetBundle);
            }

            _RefreshProviderMenuState();
        }

        [MenuItem(UseAssetDatabaseMenu, true)]
        public static bool ValidateUseAssetDatabaseProvider()
        {
            _RefreshProviderMenuState();
            return true;
        }

        [MenuItem(UseAssetBundleMenu, true)]
        public static bool ValidateUseAssetBundleProvider()
        {
            _RefreshProviderMenuState();
            return true;
        }

        private static void _RefreshProviderMenuState()
        {
            var providerMode = AssetManager.GetConfiguredProviderMode();
            Menu.SetChecked(UseAssetDatabaseMenu, providerMode == AssetProviderMode.AssetDatabase);
            Menu.SetChecked(UseAssetBundleMenu, providerMode == AssetProviderMode.AssetBundle);
        }

        private static void BuildAssetBundleForTarget(BuildTarget buildTarget, string platformDirectoryName)
        {
            try
            {
                var outputDirectory = Path.Combine(
                    AssetPathUtility.GetStreamingAssetBundleRootDirectory(),
                    platformDirectoryName);
                Directory.CreateDirectory(outputDirectory);

                IAssetBundleBuildStrategy buildStrategy = new SingleBundleBuildStrategy();
                var builds = buildStrategy.CollectBuilds();
                var manifest = BuildPipeline.BuildAssetBundles(
                    outputDirectory,
                    builds,
                    BuildAssetBundleOptions.None,
                    buildTarget);

                if (manifest == null)
                {
                    throw new InvalidOperationException("BuildPipeline.BuildAssetBundles returned null.");
                }

                AssetDatabase.Refresh();
                Debug.Log(
                    "AssetBundle build finished. Target="
                    + buildTarget
                    + ", output="
                    + outputDirectory);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Build AssetBundle Failed", exception.Message, "OK");
            }
        }
    }
}
