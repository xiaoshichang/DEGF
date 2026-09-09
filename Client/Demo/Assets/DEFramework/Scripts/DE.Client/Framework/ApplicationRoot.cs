using Assets.Scripts.DE.Client.Asset;
using Assets.Scripts.DE.Client.Core;
using Assets.Scripts.DE.Client.Network;
using Assets.Scripts.DE.Client.UI;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Assets.Scripts.DE.Client.Framework
{


    public class ApplicationRoot : MonoBehaviour
    {
        public static void RegisterGameplay(Assembly assembly, Func<GameInstance> factory)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            if (_GameInstanceFactory != null)
            {
                throw new InvalidOperationException("Gameplay is already registered.");
            }

            _RegisteredGameplayAssembly = assembly;
            _GameInstanceFactory = factory;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetGameplayRegistration()
        {
            _RegisteredGameplayAssembly = null;
            _GameInstanceFactory = null;
        }

        private void _CollectAssemblies()
        {
            if (_RegisteredGameplayAssembly == null || _GameInstanceFactory == null)
            {
                throw new InvalidOperationException(
                    "Gameplay has not been registered. Call ApplicationRoot.RegisterGameplay before Awake.");
            }

            _Assemblies.Clear();
            _Assemblies.Add(typeof(ApplicationRoot).Assembly);

            if (!_Assemblies.Contains(_RegisteredGameplayAssembly))
            {
                _Assemblies.Add(_RegisteredGameplayAssembly);
            }

            DELogger.Info("ApplicationRoot", $"{_Assemblies.Count} assemblies collected.");
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

        private void _InitGameInstance()
        {
            _GameInstance = _GameInstanceFactory();
            if (_GameInstance == null)
            {
                throw new InvalidOperationException("GameInstance factory returned null.");
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

        private void _UninitGameInstance()
        {
            _GameInstance?.UnInit();
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
            _UIManager?.UnInit();
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
            _AssetManager?.UnInit();
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
            _GMSystem?.UnInit();
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
            _GameInstance?.Update();
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


        private static Assembly _RegisteredGameplayAssembly;
        private static Func<GameInstance> _GameInstanceFactory;
        private List<Assembly> _Assemblies = new List<Assembly>();
        private UIManager _UIManager;
        private AssetManager _AssetManager;
        private NetworkManager _NetworkManager;
        private AuthSystem _AuthSystem;
        private GameInstance _GameInstance;
        private GMSystem _GMSystem;
    }
}
