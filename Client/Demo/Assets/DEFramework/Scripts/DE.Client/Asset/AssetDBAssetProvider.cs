using Assets.Scripts.DE.Client.Core;
using System;
using System.Collections;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Assets.Scripts.DE.Client.Asset
{
    internal sealed class AssetDBAsyncRunner : MonoBehaviour
    {
    }

    public sealed class AssetDBAssetProvider : AssetProvider
    {
        private static AssetDBAsyncRunner s_asyncRunner;

        public override AssetProviderMode Mode => AssetProviderMode.AssetDatabase;

        public override void Init()
        {
            base.Init();

#if UNITY_EDITOR
            _EnsureAsyncRunner();
#endif
        }

        public override void LoadAsync(AssetLoadHandle handle)
        {
            if (handle == null)
            {
                throw new ArgumentNullException(nameof(handle));
            }

#if UNITY_EDITOR
            _EnsureAsyncRunner().StartCoroutine(_LoadAsyncCoroutine(handle));
#else
            handle.Complete(null, "AssetDatabase provider is only available inside the Unity Editor.");
#endif
        }

        public override void WaitForCompletion(AssetLoadHandle handle)
        {
            if (handle == null || handle.IsDone)
            {
                return;
            }

#if UNITY_EDITOR
            _LoadImmediate(handle);
#else
            handle.Complete(null, "AssetDatabase provider is only available inside the Unity Editor.");
#endif
        }

        public override void Release(AssetLoadHandle handle)
        {
            if (handle == null)
            {
                return;
            }
        }

#if UNITY_EDITOR
        private static AssetDBAsyncRunner _EnsureAsyncRunner()
        {
            if (s_asyncRunner != null)
            {
                return s_asyncRunner;
            }

            var runnerNode = new GameObject("AssetDBAsyncRunner");
            runnerNode.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(runnerNode);
            s_asyncRunner = runnerNode.AddComponent<AssetDBAsyncRunner>();
            return s_asyncRunner;
        }

        private static IEnumerator _LoadAsyncCoroutine(AssetLoadHandle handle)
        {
            yield return null;

            if (handle == null || handle.IsDone || handle.IsReleased)
            {
                yield break;
            }

            _LoadImmediate(handle);
        }

        private static void _LoadImmediate(AssetLoadHandle handle)
        {
            var assetDatabasePath = AssetPathUtility.ToAssetDatabasePath(handle.AssetPath);
            var assetObject = AssetDatabase.LoadAssetAtPath(assetDatabasePath, handle.AssetType);
            if (assetObject == null)
            {
                handle.Complete(
                    null,
                    "AssetDatabase load failed, path=" + assetDatabasePath + ", type=" + handle.AssetType.Name + ".");
                return;
            }

            handle.Complete(assetObject, string.Empty);
        }
#endif
    }
}
