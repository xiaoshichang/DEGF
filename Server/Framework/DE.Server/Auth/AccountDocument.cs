using System;
using MongoDB.Bson.Serialization.Attributes;

namespace DE.Server.Auth
{
    public sealed class AccountDocument
    {
        [BsonId]
        public string Id { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
