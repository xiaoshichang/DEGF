using Assets.Scripts.DE.Client.Asset;
using Assets.Scripts.DE.Client.Core;
using Assets.Scripts.DE.Client.Network;
using Assets.Scripts.DE.Client.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Assets.Scripts.DE.Client.Framework
{


    public class ApplicationRoot : MonoBehaviour
    {
        private void _CollectAssemblies()
        {
            _Assemblies.Clear();
            _GameplayAssemblies.Clear();

            var frameworkAssembly = Assembly.GetExecutingAssembly();
            _Assemblies.Add(frameworkAssembly);

            foreach (string name in AssemblyNameList)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    DELogger.Warn("ApplicationRoot", "Ignore empty gameplay assembly name.");
                    continue;
                }

                var gameplayAssembly = Assembly.Load(name);
                _Assemblies.Add(gameplayAssembly);
                _GameplayAssemblies.Add(gameplayAssembly);
            }

            DELogger.Info("ApplicationRoot", $"{_Assemblies.Count} assemblies collected.");
            DELogger.Info("ApplicationRoot", $"{_GameplayAssemblies.Count} gameplay assemblies collected.");
        }

        private void _InitLogger()
        {
            var clientID = Application.productName + DateTime.Now.ToString("_yyyyMMdd_HHmmss");
            DELogger.Init(clientID, null);
            DELogger.Info($"DELogger initialized, log dir: {DELogger.LogDirectory}, log file: {DELogger.FileName}");
        }

        private void _UninitLogger()
        {
            if (!DELogger.IsInitialized())
            {
                return;
            }

            DELogger.Uninit();
        }

        private void _SearchGameInstanceType()
        {
            _GameInstanceType = null;

            foreach (var assembly in _GameplayAssemblies)
            {
                foreach (var type in _GetAssemblyTypes(assembly))
                {
                    if (type == null || type.IsAbstract || type.IsGenericTypeDefinition)
                    {
                        continue;
                    }

                    if (!typeof(GameInstance).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    if (_GameInstanceType != null)
                    {
                        throw new InvalidOperationException(
                            "Multiple GameInstance types found. first="
                            + _GameInstanceType.FullName
                            + ", second="
                            + type.FullName
                            + ".");
                    }

                    _GameInstanceType = type;
                }
            }

            if (_GameInstanceType == null)
            {
                throw new InvalidOperationException("No GameInstance type found in gameplay assemblies.");
            }

            DELogger.Info("ApplicationRoot", "GameInstance type found: " + _GameInstanceType.FullName + ".");
        }

        private void _InitGameInstance()
        {
            _SearchGameInstanceType();

            _GameInstance = Activator.CreateInstance(_GameInstanceType) as GameInstance;
            if (_GameInstance == null)
            {
                throw new InvalidOperationException("Create GameInstance failed, type=" + _GameInstanceType.FullName + ".");
            }

            GameInstance.Instance = _GameInstance;
            _GameInstance.Init();
        }

        private void _InitNetworkManager()
        {
            _NetworkManager = new NetworkManager();
            _NetworkManager.Init();
            NetworkManager.Instance = _NetworkManager;
        }

        private void _InitAuthSystem()
        {
            _AuthSystem = new AuthSystem();
            _AuthSystem.Init();
            AuthSystem.Instance = _AuthSystem;
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
                DELogger.Warn("ApplicationRoot", $"Load types from assembly '{assembly.FullName}' partially failed: {exception.Message}");
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

        private void _UninitGameInstance()
        {
            _GameInstance.UnInit();
            GameInstance.Instance = _GameInstance;
            _GameInstance = null;
            GameInstance.Instance = null;
        }

        private void _UninitNetworkManager()
        {
            if (_NetworkManager == null)
            {
                return;
            }

            _NetworkManager.UnInit();
            _NetworkManager = null;
            NetworkManager.Instance = null;
        }

        private void _UninitAuthSystem()
        {
            if (_AuthSystem == null)
            {
                return;
            }

            _AuthSystem.UnInit();
            _AuthSystem = null;
            AuthSystem.Instance = null;
        }

        private void _InitUIManager()
        {
            _UIManager = new UIManager();
            _UIManager.Init();
            UIManager.Instance = _UIManager;
        }

        private void _UninitUIManager()
        {
            _UIManager.UnInit();
            _UIManager = null;
        }

        private void _InitAssetManager()
        {
            _AssetManager = new AssetManager();
            _AssetManager.Init();
            AssetManager.Instance = _AssetManager;
        }

        private void _UninitAssetManager()
        {
            _AssetManager.UnInit();
            _AssetManager = null;
            AssetManager.Instance = null;
        }

        private void _InitGMSystem()
        {
            _GMSystem = new GMSystem();
            _GMSystem.Init(_Assemblies);
            GMSystem.Instance = _GMSystem;
        }

        private void _UninitGMSystem()
        {
            _GMSystem.UnInit();
            _GMSystem = null;
        }

        void Awake()
        {
            _InitLogger();
            DELogger.Info("ApplicationRoot Awake");

            _CollectAssemblies();
            _InitAssetManager();
            _InitUIManager();
            _InitNetworkManager();
            _InitAuthSystem();
            _InitGMSystem();
            _InitGameInstance();
        }

        // Use this for initialization
        void Start()
        {
        }

        // Update is called once per frame
        void Update()
        {
            _NetworkManager?.TickIncoming();
            _GameInstance.Update();
            _NetworkManager?.TickOutgoing();
        }

        private void OnDestroy()
        {
            _UninitGameInstance();
            _UninitGMSystem();
            _UninitAuthSystem();
            _UninitNetworkManager();
            _UninitUIManager();
            _UninitAssetManager();
            
            DELogger.Info("ApplicationRoot OnDestroy");
            _UninitLogger();
        }


        public List<string> AssemblyNameList = new List<string>();
        private List<Assembly> _Assemblies = new List<Assembly>();
        private List<Assembly> _GameplayAssemblies = new List<Assembly>();
        private UIManager _UIManager;
        private AssetManager _AssetManager;
        private NetworkManager _NetworkManager;
        private AuthSystem _AuthSystem;
        private GameInstance _GameInstance;
        private GMSystem _GMSystem;
        private Type _GameInstanceType;
    }
}
