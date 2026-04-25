using System.Collections;
using UnityEngine;

namespace Assets.Scripts.DE.Client.Application
{
    public static class Global
    {
        public static GameInstance GameInstance;
    }


    public class ApplicationRoot : MonoBehaviour
    {
        void Awake()
        {
            _GameInstance = new GameInstance();
            Global.GameInstance = _GameInstance;
            _GameInstance.Init();
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
            _GameInstance.UnInit();
            _GameInstance = null;
        }

        private GameInstance _GameInstance;
    }
}