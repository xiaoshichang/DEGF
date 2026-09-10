using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading.Tasks;
using DE.Server.Database;
using DE.Server.Entities;
using DE.Share.Data;
using DE.Share.Data.DataProvider;

namespace DE.Server.NativeBridge
{
    public enum ManagedRuntimeServerType
    {
        Unknown,
        Gm,
        Gate,
        Game,
    }

    /// <summary>
    /// global state of the managed runtime
    /// </summary>
    public sealed class ManagedRuntimeState
    {
        private AssemblyDependencyResolver _gameplayAssemblyDependencyResolver;
        private string _gameplayAssemblyDirectory = string.Empty;
        private string _frameworkAssemblyDirectory = string.Empty;
        private bool _gameplayAssemblyResolverRegistered;
        private bool _unhandledExceptionHandlersRegistered;

        public static ManagedRuntimeState Current { get; private set; }

        public static bool IsInitialized => Current != null;

        public string ServerId { get; private set; } = string.Empty;
        public string ConfigPath { get; private set; } = string.Empty;
        public string FrameworkDllPath { get; private set; } = string.Empty;
        public string GameplayDllPath { get; private set; } = string.Empty;
        public Assembly GameplayAssembly { get; private set; }
        public ManagedClusterConfig ClusterConfig { get; private set; }
        public List<Type> StubTypes { get; private set; } = new();
        public ManagedRuntimeServerType ServerType { get; private set; }
        public GmCommandRuntimeState GmCommandRuntimeState { get; private set; }
        public GateServerRuntimeState GateServerRuntimeState { get; private set; }
        public GameServerRuntimeState GameServerRuntimeState { get; private set; }
        public DatabaseService DatabaseService { get; private set; }
        public ServerStubDistributeTable StubDistributeTable { get; private set; } = new();

        private ManagedRuntimeState()
        {
        }

        public static void Initialize(ManagedRuntimeInitInfo info)
        {
            if (Current != null)
            {
                return;
            }
            var runtimeState = new ManagedRuntimeState();
            try
            {
                runtimeState.InitializeCore(info);
            }
            catch
            {
                runtimeState.UninitializeCore();
                throw;
            }
            Current = runtimeState;
        }

        public static void Uninitialize()
        {
            if (Current == null)
            {
                return;
            }
            Current.UninitializeCore();
            Current = null;
        }

        public static ManagedRuntimeState RequireCurrent()
        {
            if (Current == null)
            {
                throw new InvalidOperationException("Managed runtime is not initialized.");
            }

            return Current;
        }

        public static GateServerRuntimeState RequireCurrentGateServerRuntimeState()
        {
            var runtimeState = RequireCurrent();
            if (runtimeState.GateServerRuntimeState == null)
            {
                throw new InvalidOperationException($"Managed runtime for server {runtimeState.ServerId} does not own gate server runtime state." );
            }

            return runtimeState.GateServerRuntimeState;
        }

        public static GmCommandRuntimeState RequireCurrentGmCommandRuntimeState()
        {
            var runtimeState = RequireCurrent();
            if (runtimeState.GmCommandRuntimeState == null)
            {
                throw new InvalidOperationException($"Managed runtime for server {runtimeState.ServerId} does not own GM command runtime state." );
            }

            return runtimeState.GmCommandRuntimeState;
        }

        public static GameServerRuntimeState RequireCurrentGameServerRuntimeState()
        {
            var runtimeState = RequireCurrent();
            if (runtimeState.GameServerRuntimeState == null)
            {
                throw new InvalidOperationException($"Managed runtime for server {runtimeState.ServerId} does not own game server runtime state.");
            }

            return runtimeState.GameServerRuntimeState;
        }

        public static ManagedRuntimeServerType ResolveServerType(string serverId)
        {
            if (string.IsNullOrWhiteSpace(serverId))
            {
                return ManagedRuntimeServerType.Unknown;
            }

            if (string.Equals(serverId, "GM", StringComparison.OrdinalIgnoreCase))
            {
                return ManagedRuntimeServerType.Gm;
            }

            if (serverId.StartsWith("Gate", StringComparison.OrdinalIgnoreCase))
            {
                return ManagedRuntimeServerType.Gate;
            }

            if (serverId.StartsWith("Game", StringComparison.OrdinalIgnoreCase))
            {
                return ManagedRuntimeServerType.Game;
            }

            return ManagedRuntimeServerType.Unknown;
        }

        public static string GetStubTypeKey(Type stubType)
        {
            if (stubType == null)
            {
                throw new ArgumentNullException(nameof(stubType));
            }

            return stubType.AssemblyQualifiedName ?? stubType.FullName ?? stubType.Name;
        }

        public void HandleStubDistribute(ServerStubDistributeTable table)
        {
            StubDistributeTable = table ?? throw new ArgumentNullException(nameof(table));
            DELogger.Info(
                nameof(ManagedRuntimeState),
                $"Stub distribute table updated on {ServerId}, nodeCount={StubDistributeTable.NodeToStubTypeKeys.Count}."
            );
        }

        public string FindStubServerId(string stubName)
        {
            if (string.IsNullOrWhiteSpace(stubName))
            {
                return string.Empty;
            }

            foreach (var pair in StubDistributeTable.NodeToStubTypeKeys)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                foreach (var stubTypeKey in pair.Value)
                {
                    if (!TryResolveStubType(stubTypeKey, out var stubType))
                    {
                        continue;
                    }

                    if (string.Equals(stubType.Name, stubName, StringComparison.Ordinal)
                        || string.Equals(stubType.FullName, stubName, StringComparison.Ordinal)
                        || string.Equals(stubTypeKey, stubName, StringComparison.Ordinal))
                    {
                        return pair.Key;
                    }
                }
            }

            return string.Empty;
        }

        public string SelectGameServerId(Guid avatarId)
        {
            var gameServerIds = GetGameServerIds();
            if (gameServerIds.Count == 0)
            {
                return string.Empty;
            }

            var index = (avatarId.GetHashCode() & int.MaxValue) % gameServerIds.Count;
            return gameServerIds[index];
        }

        /// <summary>
        /// The same route key maps to the same gate, keeping RPC order stable per route key.
        /// </summary>
        public string SelectGateServerId(string routeKey)
        {
            var gateServerIds = GetGateServerIds();
            if (gateServerIds.Count == 0)
            {
                return string.Empty;
            }

            var hash = string.IsNullOrEmpty(routeKey)
                ? 0
                : routeKey.GetHashCode();
            var index = (hash & int.MaxValue) % gateServerIds.Count;
            return gateServerIds[index];
        }

        public IReadOnlyList<string> GetGameServerIds()
        {
            var gameServerIds = new List<string>();
            var gameNodes = ClusterConfig?.Game;
            if (gameNodes == null)
            {
                return gameServerIds;
            }

            foreach (var property in gameNodes)
            {
                if (!string.IsNullOrWhiteSpace(property.Key))
                {
                    gameServerIds.Add(property.Key);
                }
            }

            gameServerIds.Sort(StringComparer.Ordinal);
            return gameServerIds;
        }

        public IReadOnlyList<string> GetGateServerIds()
        {
            var gateServerIds = new List<string>();
            var gateNodes = ClusterConfig?.Gate;
            if (gateNodes == null)
            {
                return gateServerIds;
            }

            foreach (var property in gateNodes)
            {
                if (!string.IsNullOrWhiteSpace(property.Key))
                {
                    gateServerIds.Add(property.Key);
                }
            }

            gateServerIds.Sort(StringComparer.Ordinal);
            return gateServerIds;
        }

        public bool TryResolveStubType(string stubTypeKey, out Type stubType)
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

        private void _RegisterUnhandledExceptionHandlers()
        {
            if (_unhandledExceptionHandlersRegistered)
            {
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += _OnUnhandledException;
            TaskScheduler.UnobservedTaskException += _OnUnobservedTaskException;
            _unhandledExceptionHandlersRegistered = true;
        }

        private void _UnregisterUnhandledExceptionHandlers()
        {
            if (!_unhandledExceptionHandlersRegistered)
            {
                return;
            }

            AppDomain.CurrentDomain.UnhandledException -= _OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= _OnUnobservedTaskException;
            _unhandledExceptionHandlersRegistered = false;
        }

        private void _OnUnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
        {
            _ = sender;

            var exception = eventArgs?.ExceptionObject as Exception;
            var exceptionText = exception?.ToString() ?? eventArgs?.ExceptionObject?.ToString() ?? "Unknown managed exception.";
            var terminating = eventArgs != null && eventArgs.IsTerminating;

            _TryReportManagedException(
                "ManagedUnhandledException",
                $"Unhandled managed exception on {ServerId}, terminating={terminating}: {exceptionText}"
            );
        }

        private void _OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs eventArgs)
        {
            _ = sender;

            try
            {
                var exceptionText = eventArgs?.Exception?.ToString() ?? "Unknown unobserved task exception.";
                _TryReportManagedException(
                    "ManagedUnhandledException",
                    $"Unobserved task exception on {ServerId}: {exceptionText}"
                );
                eventArgs?.SetObserved();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
            }
        }

        private void _TryReportManagedException(string tag, string message)
        {
            try
            {
                Console.Error.WriteLine(message);
                DELogger.Error(tag, message);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
            }
        }

        private void _RegisterGameplayAssemblyResolver(string gameplayDllPath)
        {
            if (_gameplayAssemblyResolverRegistered)
            {
                return;
            }

            _gameplayAssemblyDependencyResolver = new AssemblyDependencyResolver(gameplayDllPath);
            _gameplayAssemblyDirectory = Path.GetDirectoryName(gameplayDllPath) ?? string.Empty;
            _frameworkAssemblyDirectory = string.IsNullOrWhiteSpace(FrameworkDllPath)
                ? string.Empty
                : Path.GetDirectoryName(Path.GetFullPath(FrameworkDllPath)) ?? string.Empty;

            AssemblyLoadContext.Default.Resolving += _ResolveGameplayAssembly;
            AssemblyLoadContext.Default.ResolvingUnmanagedDll += ResolveGameplayUnmanagedDll;
            _gameplayAssemblyResolverRegistered = true;

            DELogger.Info(
                nameof(ManagedRuntimeState),
                $"Registered gameplay assembly resolver for {gameplayDllPath}."
            );
        }

        private void UnregisterGameplayAssemblyResolver()
        {
            if (!_gameplayAssemblyResolverRegistered)
            {
                return;
            }

            AssemblyLoadContext.Default.Resolving -= _ResolveGameplayAssembly;
            AssemblyLoadContext.Default.ResolvingUnmanagedDll -= ResolveGameplayUnmanagedDll;
            _gameplayAssemblyDependencyResolver = null;
            _gameplayAssemblyDirectory = string.Empty;
            _frameworkAssemblyDirectory = string.Empty;
            _gameplayAssemblyResolverRegistered = false;
        }

        private Assembly _ResolveGameplayAssembly(AssemblyLoadContext loadContext, AssemblyName assemblyName)
        {
            foreach (var loadedAssembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (loadedAssembly != null && AssemblyName.ReferenceMatchesDefinition(loadedAssembly.GetName(), assemblyName))
                {
                    return loadedAssembly;
                }
            }

            var resolvedAssemblyPath = _gameplayAssemblyDependencyResolver?.ResolveAssemblyToPath(assemblyName);
            if (string.IsNullOrWhiteSpace(resolvedAssemblyPath))
            {
                resolvedAssemblyPath = _ResolveAssemblyPathFromKnownDirectories(assemblyName);
            }

            if (string.IsNullOrWhiteSpace(resolvedAssemblyPath) || !File.Exists(resolvedAssemblyPath))
            {
                return null;
            }

            DELogger.Info( nameof(ManagedRuntimeState), $"Resolved gameplay dependency {assemblyName.FullName} -> {resolvedAssemblyPath}.");
            return loadContext.LoadFromAssemblyPath(resolvedAssemblyPath);
        }

        private string _ResolveAssemblyPathFromKnownDirectories(AssemblyName assemblyName)
        {
            var simpleAssemblyName = assemblyName?.Name;
            if (string.IsNullOrWhiteSpace(simpleAssemblyName))
            {
                return null;
            }

            var candidateFileName = simpleAssemblyName + ".dll";
            if (!string.IsNullOrWhiteSpace(_gameplayAssemblyDirectory))
            {
                var gameplayCandidatePath = Path.Combine(_gameplayAssemblyDirectory, candidateFileName);
                if (File.Exists(gameplayCandidatePath))
                {
                    return gameplayCandidatePath;
                }
            }

            if (!string.IsNullOrWhiteSpace(_frameworkAssemblyDirectory))
            {
                var frameworkCandidatePath = Path.Combine(_frameworkAssemblyDirectory, candidateFileName);
                if (File.Exists(frameworkCandidatePath))
                {
                    return frameworkCandidatePath;
                }
            }

            return null;
        }

        private IntPtr ResolveGameplayUnmanagedDll(Assembly assembly, string unmanagedDllName)
        {
            _ = assembly;

            if (string.IsNullOrWhiteSpace(unmanagedDllName))
            {
                return IntPtr.Zero;
            }

            var resolvedUnmanagedDllPath = _gameplayAssemblyDependencyResolver?.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (string.IsNullOrWhiteSpace(resolvedUnmanagedDllPath) || !File.Exists(resolvedUnmanagedDllPath))
            {
                return IntPtr.Zero;
            }

            DELogger.Info(nameof(ManagedRuntimeState), $"Resolved gameplay native dependency {unmanagedDllName} -> {resolvedUnmanagedDllPath}.");
            return NativeLibrary.Load(resolvedUnmanagedDllPath);
        }

        private void _LoadGameplayAssemblies()
        {
            if (string.IsNullOrEmpty(GameplayDllPath))
            {
                return;
            }

            var gameplayDllPath = Path.GetFullPath(GameplayDllPath);
            if (!File.Exists(gameplayDllPath))
            {
                DELogger.Warn(nameof(ManagedRuntimeState), $"Gameplay dll not found: {gameplayDllPath}");
                return;
            }

            _RegisterGameplayAssemblyResolver(gameplayDllPath);
            GameplayAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(gameplayDllPath);
        }

        private void _InitDataRuntime()
        {
            DataRuntime.Initialize(new ExcelDataProvider());
            DataRuntime.SetRootDirectory(ClusterConfig.DataRoot);
        }

        private void InitializeCore(ManagedRuntimeInitInfo info)
        {
            ServerId = _RequireNonEmpty(info.ServerId, nameof(info.ServerId));
            ConfigPath = _RequireNonEmpty(info.ConfigPath, nameof(info.ConfigPath));
            FrameworkDllPath = _RequireNonEmpty(info.FrameworkDllPath, nameof(info.FrameworkDllPath));
            GameplayDllPath = _RequireNonEmpty(info.GameplayDllPath, nameof(info.GameplayDllPath));
            ServerType = ResolveServerType(ServerId);

            _RegisterUnhandledExceptionHandlers();
            _LoadGameplayAssemblies();
            ClusterConfig = ManagedClusterConfig.Load(ConfigPath);
            DatabaseService = new DatabaseService(ClusterConfig.Database);
            _InitDataRuntime();

            var assemblies = new[] { GameplayAssembly, Assembly.GetExecutingAssembly() };
            StubTypes = ServerStubTypeCollector.CollectAllStubTypes(assemblies);

            if (ServerType == ManagedRuntimeServerType.Gm)
            {
                GmCommandRuntimeState = new GmCommandRuntimeState(this);
            }
            else if (ServerType == ManagedRuntimeServerType.Gate)
            {
                GateServerRuntimeState = new GateServerRuntimeState(this);
            }
            else if (ServerType == ManagedRuntimeServerType.Game)
            {
                GameServerRuntimeState = new GameServerRuntimeState(this);
            }

            DELogger.Info(nameof(ManagedRuntimeState), $"Managed runtime initialized for {ServerId} with server type {ServerType}.");
        }

        private void UninitializeCore()
        {
            try
            {
                GmCommandRuntimeState?.Uninitialize();
                GateServerRuntimeState?.Uninitialize();
                GameServerRuntimeState?.Uninitialize();
            }
            finally
            {
                DatabaseService = null;
                _UnregisterUnhandledExceptionHandlers();
                UnregisterGameplayAssemblyResolver();
                DataRuntime.Shutdown();

                GmCommandRuntimeState = null;
                GateServerRuntimeState = null;
                GameServerRuntimeState = null;
                ClusterConfig = null;
                StubDistributeTable = new ServerStubDistributeTable();
                StubTypes = new List<Type>();
                GameplayAssembly = null;
                ServerType = ManagedRuntimeServerType.Unknown;
                ServerId = string.Empty;
                ConfigPath = string.Empty;
                FrameworkDllPath = string.Empty;
                GameplayDllPath = string.Empty;
            }
        }

        private static string _RequireNonEmpty(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Managed runtime init info field must not be empty.", name);
            }

            return value.Trim();
        }
    }
}
