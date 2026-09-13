using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DE.Server.Database;

namespace DE.Server.NativeBridge
{
    public sealed class ManagedAssemblyConfig
    {
        /// <summary>
        /// Gets or sets the managed assemblies that must be loaded before framework runtime initialization.
        /// Relative paths are resolved from the cluster configuration file directory.
        /// </summary>
        [JsonPropertyName("preloadDlls")]
        public List<string> PreloadDlls { get; set; } = new();

        internal void ResolvePreloadDllPaths(string configDirectory)
        {
            if (PreloadDlls == null)
            {
                PreloadDlls = new List<string>();
                return;
            }

            var pathComparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var resolvedPaths = new List<string>(PreloadDlls.Count);
            var uniquePaths = new HashSet<string>(pathComparer);
            for (var index = 0; index < PreloadDlls.Count; ++index)
            {
                var configuredPath = PreloadDlls[index];
                if (string.IsNullOrWhiteSpace(configuredPath))
                {
                    throw new InvalidOperationException(
                        $"Managed cluster config field 'managed.preloadDlls[{index}]' is empty."
                    );
                }

                var resolvedPath = Path.GetFullPath(Path.Combine(configDirectory, configuredPath.Trim()));
                if (!File.Exists(resolvedPath))
                {
                    throw new FileNotFoundException(
                        $"Managed preload dll '{resolvedPath}' does not exist.",
                        resolvedPath
                    );
                }

                if (!uniquePaths.Add(resolvedPath))
                {
                    throw new InvalidOperationException(
                        $"Managed cluster config field 'managed.preloadDlls' contains duplicate path '{resolvedPath}'."
                    );
                }

                resolvedPaths.Add(resolvedPath);
            }

            PreloadDlls = resolvedPaths;
        }
    }

    public sealed class ManagedClusterConfig
    {
        private static readonly JsonSerializerOptions s_jsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        [JsonPropertyName("database")]
        public DatabaseConfig Database { get; set; }

        [JsonPropertyName("dataRoot")]
        public string DataRoot { get; set; } = string.Empty;

        [JsonPropertyName("managed")]
        public ManagedAssemblyConfig Managed { get; set; }

        [JsonPropertyName("gate")]
        public Dictionary<string, JsonElement> Gate { get; set; } = new(StringComparer.Ordinal);

        [JsonPropertyName("game")]
        public Dictionary<string, JsonElement> Game { get; set; } = new(StringComparer.Ordinal);

        public static ManagedClusterConfig Load(string configPath)
        {
            if (string.IsNullOrWhiteSpace(configPath))
            {
                throw new InvalidOperationException("Managed cluster config path is empty.");
            }

            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException("Managed cluster config file not found.", configPath);
            }

            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<ManagedClusterConfig>(json, s_jsonSerializerOptions);
            if (config == null)
            {
                throw new InvalidOperationException("Managed cluster config is invalid.");
            }

            if (string.IsNullOrWhiteSpace(config.DataRoot))
            {
                throw new InvalidOperationException("Managed cluster config field 'dataRoot' is missing or empty.");
            }

            string configDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath));
            config.DataRoot = Path.GetFullPath(Path.Combine(configDirectory, config.DataRoot));
            if (!Directory.Exists(config.DataRoot))
            {
                throw new DirectoryNotFoundException($"Managed cluster config dataRoot directory '{config.DataRoot}' does not exist.");
            }

            if (config.Managed == null)
            {
                throw new InvalidOperationException("Managed cluster config section 'managed' is missing.");
            }

            config.Managed.ResolvePreloadDllPaths(configDirectory);

            if (config.Database == null)
            {
                throw new InvalidOperationException("Managed cluster config section 'database' is missing.");
            }

            config.Database.Validate();
            if (config.Gate == null)
            {
                config.Gate = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            }

            if (config.Game == null)
            {
                config.Game = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            }

            return config;
        }
    }
}
