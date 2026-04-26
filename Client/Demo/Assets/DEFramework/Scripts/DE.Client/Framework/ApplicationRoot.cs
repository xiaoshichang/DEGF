using Assets.Scripts.DE.Client.Core;
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
            _Assemblies.Add(Assembly.GetExecutingAssembly());
            foreach(string name in AssemblyNameList)
            {
                _Assemblies.Add(Assembly.Load(name));
            }
            DELogger.Info($"{_Assemblies.Count} Assemblies collected");
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
            _GameInstance = new GameInstance();
            _GameInstance.Init();
            GameInstance.Instance = _GameInstance;
        }

        private void _UninitGameInstance()
        {
            _GameInstance.UnInit();
            _GameInstance = null;
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
            _InitUIManager();
            _InitGameInstance();
            _InitGMSystem();
        }

        // Use this for initialization
        void Start()
        {
        }

        // Update is called once per frame
        void Update()
        {
            _GameInstance.Update();
        }

        private void OnDestroy()
        {
            DELogger.Info("ApplicationRoot OnDestroy");

            _UninitGMSystem();
            _UninitGameInstance();
            _UninitUIManager();
            _UninitLogger();
        }


        public List<string> AssemblyNameList = new List<string>();
        private List<Assembly> _Assemblies = new List<Assembly>();
        private UIManager _UIManager;
        private GameInstance _GameInstance;
        private GMSystem _GMSystem;
    }
}
