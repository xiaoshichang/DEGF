using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DE.Server.Database;

namespace DE.Server.NativeBridge
{
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
