using System;

namespace DE.Server.Database
{
    public sealed class DatabaseConfig
    {
        public string Provider { get; set; } = "MongoDB";
        public string ConnectionString { get; set; } = "mongodb://127.0.0.1:27017";
        public string ConnectionStringEnv { get; set; } = "DEGF_MONGODB_URI";
        public string DatabaseName { get; set; } = "degf_local_dev";
        public int MinConnectionPoolSize { get; set; }
        public int MaxConnectionPoolSize { get; set; } = 100;
        public int ConnectTimeoutMs { get; set; } = 10000;
        public int ServerSelectionTimeoutMs { get; set; } = 10000;
        public int WaitQueueTimeoutMs { get; set; } = 30000;
        public int OperationTimeoutMs { get; set; } = 3000;

        public string ResolveConnectionString()
        {
            if (!string.IsNullOrWhiteSpace(ConnectionStringEnv))
            {
                var envValue = Environment.GetEnvironmentVariable(ConnectionStringEnv);
                if (!string.IsNullOrWhiteSpace(envValue))
                {
                    return envValue.Trim();
                }
            }

            return ConnectionString == null ? string.Empty : ConnectionString.Trim();
        }

        public void Validate()
        {
            if (!string.Equals(Provider, "MongoDB", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Unsupported database provider: " + Provider);
            }

            if (string.IsNullOrWhiteSpace(ResolveConnectionString()))
            {
                throw new InvalidOperationException("MongoDB connection string is empty.");
            }

            if (string.IsNullOrWhiteSpace(DatabaseName))
            {
                throw new InvalidOperationException("MongoDB database name is empty.");
            }

            if (MinConnectionPoolSize < 0)
            {
                throw new InvalidOperationException("MongoDB min connection pool size must not be negative.");
            }

            if (MaxConnectionPoolSize <= 0)
            {
                throw new InvalidOperationException("MongoDB max connection pool size must be greater than zero.");
            }

            if (MinConnectionPoolSize > MaxConnectionPoolSize)
            {
                throw new InvalidOperationException("MongoDB min connection pool size must not exceed max connection pool size.");
            }

            if (ConnectTimeoutMs <= 0 || ServerSelectionTimeoutMs <= 0 || WaitQueueTimeoutMs <= 0 || OperationTimeoutMs <= 0)
            {
                throw new InvalidOperationException("MongoDB timeout settings must be greater than zero.");
            }
        }
    }
}
