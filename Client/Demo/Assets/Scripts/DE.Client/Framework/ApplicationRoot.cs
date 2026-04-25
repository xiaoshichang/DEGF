using Assets.Scripts.DE.Client.Core;
using System;
using System.Collections;
using UnityEngine;

namespace Assets.Scripts.DE.Client.Framework
{
    public static class Global
    {
        public static GameInstance GameInstance;
    }



    public class ApplicationRoot : MonoBehaviour
    {

        private void _InitLogger()
        {
            var config = new LoggingConfig();
            var clientID = Application.productName + DateTime.Now.ToString("_yyyyMMdd_HHmmss");
            DELogger.Init(clientID, config);
            DELogger.Info($"DELogger initialized, log dir: {config.RootDir}, log file: {clientID}");
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
            Global.GameInstance = _GameInstance;
            _GameInstance.Init();
        }

        private void _UninitGameInstance()
        {
            _GameInstance.UnInit();
            _GameInstance = null;
        }

        void Awake()
        {
            _InitLogger();
            DELogger.Info("ApplicationRoot Awake");
            _InitGameInstance();
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
            _UninitGameInstance();
            _UninitLogger();
        }

        private GameInstance _GameInstance;
    }
}