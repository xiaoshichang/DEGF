using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using DE.Server.Entities;

namespace DE.Server.NativeBridge
{
    /// <summary>
    /// global state of the managed runtime
    /// </summary>
    public static class ManagedRuntimeState
    {
        private static AssemblyDependencyResolver s_gameplayAssemblyDependencyResolver;
        private static string s_gameplayAssemblyDirectory = string.Empty;
        private static string s_frameworkAssemblyDirectory = string.Empty;
        private static bool s_gameplayAssemblyResolverRegistered;

        public static bool IsInitialized { get; private set; }
        public static string ServerId { get; private set; } = string.Empty;
        public static string ConfigPath { get; private set; } = string.Empty;
        public static string FrameworkDllPath { get; private set; } = string.Empty;
        public static string GameplayDllPath { get; private set; } = string.Empty;
        public static Assembly GameplayAssembly { get; private set; }
        
        /// <summary>
        /// all stub types
        /// </summary>
        public static List<Type> StubTypes = new List<Type>();
        
        /// <summary>
        /// key: stub type, value: last ready time
        /// </summary>
        public static Dictionary<Type, DateTime> ReadyStubs = new Dictionary<Type, DateTime>();

        /// <summary>
        /// key: stub type, value: stub instance
        /// </summary>
        public static Dictionary<Type, ServerStubEntity> StubInstances = new Dictionary<Type, ServerStubEntity>();
        
        /// <summary>
        /// stub distribute table
        /// </summary>
        public static ServerStubDistributeTable StubDistributeTable = new ServerStubDistributeTable();

        public static string GetStubTypeKey(Type stubType)
        {
            if (stubType == null)
            {
                throw new ArgumentNullException(nameof(stubType));
            }

            return stubType.AssemblyQualifiedName ?? stubType.FullName ?? stubType.Name;
        }

        public static bool TryResolveStubType(string stubTypeKey, out Type stubType)
        {
            stubType = null;
            if (string.IsNullOrWhiteSpace(stubTypeKey))
            {
                return false;
            }

            var resolvedType = Type.GetType(stubTypeKey, false);
            if (resolvedType != null && typeof(ServerStubEntity).IsAssignableFrom(resolvedType))
            {
                stubType = resolvedType;
                return true;
            }

            foreach (var candidateType in StubTypes)
            {
                if (candidateType == null)
                {
                    continue;
                }

                if (string.Equals(GetStubTypeKey(candidateType), stubTypeKey, StringComparison.Ordinal)
                    || string.Equals(candidateType.FullName, stubTypeKey, StringComparison.Ordinal))
                {
                    stubType = candidateType;
                    return true;
                }
            }

            return false;
        }

        private static void _RegisterGameplayAssemblyResolver(string gameplayDllPath)
        {
            if (s_gameplayAssemblyResolverRegistered)
            {
                return;
            }

            s_gameplayAssemblyDependencyResolver = new AssemblyDependencyResolver(gameplayDllPath);
            s_gameplayAssemblyDirectory = Path.GetDirectoryName(gameplayDllPath) ?? string.Empty;
            s_frameworkAssemblyDirectory = string.IsNullOrWhiteSpace(FrameworkDllPath)
                ? string.Empty
                : Path.GetDirectoryName(Path.GetFullPath(FrameworkDllPath)) ?? string.Empty;

            AssemblyLoadContext.Default.Resolving += _ResolveGameplayAssembly;
            AssemblyLoadContext.Default.ResolvingUnmanagedDll += _ResolveGameplayUnmanagedDll;
            s_gameplayAssemblyResolverRegistered = true;

            DELogger.Info(
                "ManagedRuntimeState",
                $"Registered gameplay assembly resolver for {gameplayDllPath}."
            );
        }

        private static void _UnregisterGameplayAssemblyResolver()
        {
            if (!s_gameplayAssemblyResolverRegistered)
            {
                return;
            }

            AssemblyLoadContext.Default.Resolving -= _ResolveGameplayAssembly;
            AssemblyLoadContext.Default.ResolvingUnmanagedDll -= _ResolveGameplayUnmanagedDll;
            s_gameplayAssemblyDependencyResolver = null;
            s_gameplayAssemblyDirectory = string.Empty;
            s_frameworkAssemblyDirectory = string.Empty;
            s_gameplayAssemblyResolverRegistered = false;
        }

        private static Assembly _ResolveGameplayAssembly(AssemblyLoadContext loadContext, AssemblyName assemblyName)
        {
            foreach (var loadedAssembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (loadedAssembly != null && AssemblyName.ReferenceMatchesDefinition(loadedAssembly.GetName(), assemblyName))
                {
                    return loadedAssembly;
                }
            }

            var resolvedAssemblyPath = s_gameplayAssemblyDependencyResolver?.ResolveAssemblyToPath(assemblyName);
            if (string.IsNullOrWhiteSpace(resolvedAssemblyPath))
            {
                resolvedAssemblyPath = _ResolveAssemblyPathFromKnownDirectories(assemblyName);
            }

            if (string.IsNullOrWhiteSpace(resolvedAssemblyPath) || !File.Exists(resolvedAssemblyPath))
            {
                return null;
            }

            DELogger.Info(
                "ManagedRuntimeState",
                $"Resolved gameplay dependency {assemblyName.FullName} -> {resolvedAssemblyPath}."
            );
            return loadContext.LoadFromAssemblyPath(resolvedAssemblyPath);
        }

        private static string _ResolveAssemblyPathFromKnownDirectories(AssemblyName assemblyName)
        {
            var simpleAssemblyName = assemblyName?.Name;
            if (string.IsNullOrWhiteSpace(simpleAssemblyName))
            {
                return null;
            }

            var candidateFileName = simpleAssemblyName + ".dll";
            if (!string.IsNullOrWhiteSpace(s_gameplayAssemblyDirectory))
            {
                var gameplayCandidatePath = Path.Combine(s_gameplayAssemblyDirectory, candidateFileName);
                if (File.Exists(gameplayCandidatePath))
                {
                    return gameplayCandidatePath;
                }
            }

            if (!string.IsNullOrWhiteSpace(s_frameworkAssemblyDirectory))
            {
                var frameworkCandidatePath = Path.Combine(s_frameworkAssemblyDirectory, candidateFileName);
                if (File.Exists(frameworkCandidatePath))
                {
                    return frameworkCandidatePath;
                }
            }

            return null;
        }

        private static IntPtr _ResolveGameplayUnmanagedDll(Assembly assembly, string unmanagedDllName)
        {
            _ = assembly;

            if (string.IsNullOrWhiteSpace(unmanagedDllName))
            {
                return IntPtr.Zero;
            }

            var resolvedUnmanagedDllPath = s_gameplayAssemblyDependencyResolver?.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (string.IsNullOrWhiteSpace(resolvedUnmanagedDllPath) || !File.Exists(resolvedUnmanagedDllPath))
            {
                return IntPtr.Zero;
            }

            DELogger.Info(
                "ManagedRuntimeState",
                $"Resolved gameplay native dependency {unmanagedDllName} -> {resolvedUnmanagedDllPath}."
            );
            return NativeLibrary.Load(resolvedUnmanagedDllPath);
        }
        
        private static void _LoadGameplayAssemblies()
        {
            if (!string.IsNullOrEmpty(GameplayDllPath))
            {
                var gameplayDllPath = Path.GetFullPath(GameplayDllPath);
                if (File.Exists(gameplayDllPath))
                {
                    _RegisterGameplayAssemblyResolver(gameplayDllPath);
                    GameplayAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(gameplayDllPath);
                }
                else
                {
                    DELogger.Warn("ManagedAPI", $"Gameplay dll not found: {gameplayDllPath}");
                }
            }
        }

        public static void Initialize(ManagedRuntimeInitInfo info)
        {
            ServerId = info.ServerId ?? string.Empty;
            ConfigPath = info.ConfigPath ?? string.Empty;
            FrameworkDllPath = info.FrameworkDllPath ?? string.Empty;
            GameplayDllPath = info.GameplayDllPath ?? string.Empty;
            ReadyStubs.Clear();
            StubInstances.Clear();
            StubDistributeTable = new ServerStubDistributeTable();
            _LoadGameplayAssemblies();
            var assemblies = new[] { GameplayAssembly, Assembly.GetExecutingAssembly() };
            StubTypes = ServerStubTypeCollector.CollectAllStubTypes(assemblies);
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
            _UnregisterGameplayAssemblyResolver();
            StubTypes = new List<Type>();
            ReadyStubs.Clear();
            StubInstances.Clear();
            StubDistributeTable = new ServerStubDistributeTable();
        }
    }
}
