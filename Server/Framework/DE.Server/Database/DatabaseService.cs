using System;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DE.Server.Database
{
    public sealed class DatabaseService
    {
        private readonly DatabaseConfig _config;
        private readonly MongoClient _client;
        private readonly IMongoDatabase _database;

        public DatabaseService(DatabaseConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _config.Validate();

            var settings = MongoClientSettings.FromConnectionString(_config.ResolveConnectionString());
            settings.MinConnectionPoolSize = _config.MinConnectionPoolSize;
            settings.MaxConnectionPoolSize = _config.MaxConnectionPoolSize;
            settings.ConnectTimeout = TimeSpan.FromMilliseconds(_config.ConnectTimeoutMs);
            settings.ServerSelectionTimeout = TimeSpan.FromMilliseconds(_config.ServerSelectionTimeoutMs);
            settings.WaitQueueTimeout = TimeSpan.FromMilliseconds(_config.WaitQueueTimeoutMs);

            _client = new MongoClient(settings);
            _database = _client.GetDatabase(_config.DatabaseName);

            PingAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public DatabaseConfig Config => _config;

        public IMongoDatabase Database => _database;

        public MongoCollection<TDocument> GetCollection<TDocument>(string collectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
            {
                throw new ArgumentException("Collection name must not be empty.", nameof(collectionName));
            }

            return new MongoCollection<TDocument>(Database.GetCollection<TDocument>(collectionName));
        }

        public CancellationTokenSource CreateOperationCancellation()
        {
            return new CancellationTokenSource(TimeSpan.FromMilliseconds(_config.OperationTimeoutMs));
        }

        private async Task PingAsync(CancellationToken cancellationToken)
        {
            using (var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(_config.OperationTimeoutMs)))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                await _database.RunCommandAsync((Command<BsonDocument>)"{ping:1}", cancellationToken: linked.Token);
            }
        }
    }
}
