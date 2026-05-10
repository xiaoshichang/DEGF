using System;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DE.Server.Database
{
    public static class DatabaseService
    {
        private static readonly object s_syncRoot = new object();
        private static DatabaseConfig s_config;
        private static MongoClient s_client;
        private static IMongoDatabase s_database;

        public static bool IsEnabled
        {
            get
            {
                return s_config != null;
            }
        }

        public static DatabaseConfig Config => s_config;

        public static IMongoDatabase Database
        {
            get
            {
                if (!IsEnabled || s_database == null)
                {
                    throw new InvalidOperationException("Database service is not initialized.");
                }

                return s_database;
            }
        }

        public static void Initialize(string configPath)
        {
            lock (s_syncRoot)
            {
                Uninitialize();

                var config = DatabaseConfigLoader.Load(configPath);
                s_config = config;
                if (config == null)
                {
                    return;
                }

                var settings = MongoClientSettings.FromConnectionString(config.ResolveConnectionString());
                settings.MinConnectionPoolSize = config.MinConnectionPoolSize;
                settings.MaxConnectionPoolSize = config.MaxConnectionPoolSize;
                settings.ConnectTimeout = TimeSpan.FromMilliseconds(config.ConnectTimeoutMs);
                settings.ServerSelectionTimeout = TimeSpan.FromMilliseconds(config.ServerSelectionTimeoutMs);
                settings.WaitQueueTimeout = TimeSpan.FromMilliseconds(config.WaitQueueTimeoutMs);

                s_client = new MongoClient(settings);
                s_database = s_client.GetDatabase(config.DatabaseName);

                PingAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
        }

        public static void Uninitialize()
        {
            s_database = null;
            s_client = null;
            s_config = null;
        }

        public static MongoCollection<TDocument> GetCollection<TDocument>(string collectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
            {
                throw new ArgumentException("Collection name must not be empty.", nameof(collectionName));
            }

            return new MongoCollection<TDocument>(Database.GetCollection<TDocument>(collectionName));
        }

        public static CancellationTokenSource CreateOperationCancellation()
        {
            if (!IsEnabled || s_config == null)
            {
                throw new InvalidOperationException("Database service is not initialized.");
            }

            return new CancellationTokenSource(TimeSpan.FromMilliseconds(s_config.OperationTimeoutMs));
        }

        private static async Task PingAsync(CancellationToken cancellationToken)
        {
            using (var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(s_config.OperationTimeoutMs)))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                await s_database.RunCommandAsync((Command<BsonDocument>)"{ping:1}", cancellationToken: linked.Token);
            }
        }
    }
}
