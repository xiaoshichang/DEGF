using System.Reflection;

namespace DE.Server.NativeBridge
{
    /// <summary>
    /// global state of the managed runtime
    /// </summary>
    public static class ManagedRuntimeState
    {
        public static bool IsInitialized { get; private set; }
        public static string ServerId { get; private set; } = string.Empty;
        public static string ConfigPath { get; private set; } = string.Empty;
        public static string FrameworkDllPath { get; private set; } = string.Empty;
        public static string GameplayDllPath { get; private set; } = string.Empty;
        public static Assembly GameplayAssembly { get; private set; }

        public static void Initialize(ManagedRuntimeInitInfo info, Assembly gameplayAssembly)
        {
            ServerId = info.ServerId ?? string.Empty;
            ConfigPath = info.ConfigPath ?? string.Empty;
            FrameworkDllPath = info.FrameworkDllPath ?? string.Empty;
            GameplayDllPath = info.GameplayDllPath ?? string.Empty;
            GameplayAssembly = gameplayAssembly;
            IsInitialized = true;
        }

        public static void Uninitialize()
        {
            IsInitialized = false;
            ServerId = string.Empty;
            ConfigPath = string.Empty;
            FrameworkDllPath = string.Empty;
            GameplayDllPath = string.Empty;
            GameplayAssembly = null;
        }
    }
}
