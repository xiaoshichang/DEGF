using Assets.Scripts.DE.Client.Asset;
using System;
using System.Linq;
using UnityEditor;

namespace Assets.Scripts.DE.Client.Editor.Asset
{
    public interface IAssetBundleBuildStrategy
    {
        AssetBundleBuild[] CollectBuilds();
    }

    public sealed class SingleBundleBuildStrategy : IAssetBundleBuildStrategy
    {
        public AssetBundleBuild[] CollectBuilds()
        {
            var assetPaths = AssetDatabase.FindAssets(string.Empty, new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !AssetDatabase.IsValidFolder(path))
                .Where(AssetPathUtility.IsBundleCandidatePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (assetPaths.Length == 0)
            {
                throw new InvalidOperationException("No buildable assets were found under any /Asb/ directory.");
            }

            var addressableNames = assetPaths
                .Select(AssetPathUtility.NormalizeLogicalAssetPath)
                .ToArray();

            return new[]
            {
                new AssetBundleBuild
                {
                    assetBundleName = AssetPathUtility.AssetBundleName,
                    assetNames = assetPaths,
                    addressableNames = addressableNames,
                },
            };
        }
    }
}
