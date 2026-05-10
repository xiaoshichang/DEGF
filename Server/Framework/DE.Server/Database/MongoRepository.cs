using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;

namespace DE.Server.Database
{
    public sealed class MongoRepository<TDocument>
    {
        private readonly IMongoCollection<TDocument> _collection;

        public MongoRepository(IMongoCollection<TDocument> collection)
        {
            _collection = collection ?? throw new ArgumentNullException(nameof(collection));
        }

        public IMongoCollection<TDocument> Collection => _collection;

        public Task InsertOneAsync(TDocument document, CancellationToken cancellationToken = default)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            return _collection.InsertOneAsync(document, null, cancellationToken);
        }

        public async Task<TDocument> FindOneAsync(FilterDefinition<TDocument> filter, CancellationToken cancellationToken = default)
        {
            return await _collection
                .Find(RequireFilter(filter))
                .Limit(1)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<TDocument>> FindManyAsync(
            FilterDefinition<TDocument> filter,
            FindOptions<TDocument, TDocument> options = null,
            CancellationToken cancellationToken = default)
        {
            using (var cursor = await _collection.FindAsync(RequireFilter(filter), options, cancellationToken))
            {
                return await cursor.ToListAsync(cancellationToken);
            }
        }

        public async Task<long> DeleteOneAsync(FilterDefinition<TDocument> filter, CancellationToken cancellationToken = default)
        {
            var result = await _collection.DeleteOneAsync(RequireFilter(filter), cancellationToken);
            return result.DeletedCount;
        }

        public async Task<long> DeleteManyAsync(FilterDefinition<TDocument> filter, CancellationToken cancellationToken = default)
        {
            var result = await _collection.DeleteManyAsync(RequireFilter(filter), cancellationToken);
            return result.DeletedCount;
        }

        public async Task<long> UpdateOneAsync(
            FilterDefinition<TDocument> filter,
            UpdateDefinition<TDocument> update,
            CancellationToken cancellationToken = default)
        {
            var result = await _collection.UpdateOneAsync(RequireFilter(filter), RequireUpdate(update), null, cancellationToken);
            return result.ModifiedCount;
        }

        public async Task<long> UpdateManyAsync(
            FilterDefinition<TDocument> filter,
            UpdateDefinition<TDocument> update,
            CancellationToken cancellationToken = default)
        {
            var result = await _collection.UpdateManyAsync(RequireFilter(filter), RequireUpdate(update), null, cancellationToken);
            return result.ModifiedCount;
        }

        public async Task ReplaceOneAsync(
            FilterDefinition<TDocument> filter,
            TDocument document,
            bool isUpsert = false,
            CancellationToken cancellationToken = default)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            await _collection.ReplaceOneAsync(
                RequireFilter(filter),
                document,
                new ReplaceOptions { IsUpsert = isUpsert },
                cancellationToken
            );
        }

        private static FilterDefinition<TDocument> RequireFilter(FilterDefinition<TDocument> filter)
        {
            if (filter == null)
            {
                throw new ArgumentNullException(nameof(filter));
            }

            return filter;
        }

        private static UpdateDefinition<TDocument> RequireUpdate(UpdateDefinition<TDocument> update)
        {
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            return update;
        }
    }
}
