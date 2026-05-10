using System.Threading;
using System.Threading.Tasks;
using DE.Server.Database;
using MongoDB.Driver;

namespace DE.Server.Auth
{
    public static class AccountCollection
    {
        public const string CollectionName = "accounts";

        public static async Task<bool> ExistsAsync(
            DatabaseService databaseService,
            string account,
            CancellationToken cancellationToken = default)
        {
            if (databaseService == null)
            {
                throw new System.ArgumentNullException(nameof(databaseService));
            }

            var normalizedAccount = NormalizeAccount(account);
            if (string.IsNullOrEmpty(normalizedAccount))
            {
                return false;
            }

            var repository = databaseService.GetCollection<AccountDocument>(CollectionName);
            var filter = Builders<AccountDocument>.Filter.Eq(document => document.Id, normalizedAccount);
            var document = await repository.FindOneAsync(filter, cancellationToken);
            return document != null;
        }

        public static string NormalizeAccount(string account)
        {
            return account == null ? string.Empty : account.Trim();
        }
    }
}
