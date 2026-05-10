using System;
using System.IO;
using System.Text.Json;

namespace DE.Server.Database
{
    public static class DatabaseConfigLoader
    {
        private static readonly JsonSerializerOptions s_jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        public static DatabaseConfig Load(string configPath)
        {
            if (string.IsNullOrWhiteSpace(configPath))
            {
                throw new InvalidOperationException("Database config path is empty.");
            }

            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException("Database config file not found.", configPath);
            }

            using (var stream = File.OpenRead(configPath))
            using (var document = JsonDocument.Parse(stream))
            {
                if (!document.RootElement.TryGetProperty("database", out var databaseElement))
                {
                    throw new InvalidOperationException("Database config section 'database' is missing.");
                }

                var config = JsonSerializer.Deserialize<DatabaseConfig>(
                    databaseElement.GetRawText(),
                    s_jsonSerializerOptions
                );
                if (config == null)
                {
                    throw new InvalidOperationException("Database config is invalid.");
                }

                config.Validate();
                return config;
            }
        }
    }
}
