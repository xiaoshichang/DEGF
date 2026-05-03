using System;
using DE.Share.Entities;
using Xunit;

namespace DE.Share.Tests
{
    public sealed partial class EntitySerializerTests
    {
        [Fact]
        public void OwnerSync_SerializesOnlyClientServerProperties()
        {
            TestEntity source = CreateSource();
            TestEntity target = new TestEntity();

            byte[] data = EntitySerializer.Serialize(source, EntitySerializeReason.OwnerSync);
            EntitySerializer.Deserialize(target, EntitySerializeReason.OwnerSync, data);

            Assert.Equal(source.Guid, target.Guid);
            Assert.Equal(source.ClientServerValue, target.ClientServerValue);
            Assert.Equal(default(int), target.AllClientsValue);
            Assert.Equal(default(int), target.ServerOnlyValue);
            Assert.Equal(default(int), target.ClientOnlyValue);
        }

        [Fact]
        public void Broadcase_SerializesOnlyAllClientsProperties()
        {
            TestEntity source = CreateSource();
            TestEntity target = new TestEntity();
            Guid originalTargetGuid = target.Guid;

            byte[] data = EntitySerializer.Serialize(source, EntitySerializeReason.Broadcase);
            EntitySerializer.Deserialize(target, EntitySerializeReason.Broadcase, data);

            Assert.Equal(originalTargetGuid, target.Guid);
            Assert.Equal(default(int), target.ClientServerValue);
            Assert.Equal(source.AllClientsValue, target.AllClientsValue);
            Assert.Equal(default(int), target.ServerOnlyValue);
            Assert.Equal(default(int), target.ClientOnlyValue);
        }

        [Fact]
        public void Migrate_SerializesServerOnlyClientServerAndAllClientsProperties()
        {
            TestEntity source = CreateSource();
            TestEntity target = new TestEntity();

            byte[] data = EntitySerializer.Serialize(source, EntitySerializeReason.Migrate);
            EntitySerializer.Deserialize(target, EntitySerializeReason.Migrate, data);

            Assert.Equal(source.Guid, target.Guid);
            Assert.Equal(source.ClientServerValue, target.ClientServerValue);
            Assert.Equal(source.AllClientsValue, target.AllClientsValue);
            Assert.Equal(source.ServerOnlyValue, target.ServerOnlyValue);
            Assert.Equal(default(int), target.ClientOnlyValue);
        }

        [Fact]
        public void TryDeserialize_ReturnsFalseWhenReasonDoesNotMatchPayload()
        {
            TestEntity source = CreateSource();
            TestEntity target = new TestEntity();

            byte[] data = EntitySerializer.Serialize(source, EntitySerializeReason.OwnerSync);
            bool result = EntitySerializer.TryDeserialize(target, EntitySerializeReason.Migrate, data);

            Assert.False(result);
        }

        private static TestEntity CreateSource()
        {
            return new TestEntity
            {
                Guid = new Guid("11111111-2222-3333-4444-555555555555"),
                ClientServerValue = 10,
                AllClientsValue = 20,
                ServerOnlyValue = 30,
                ClientOnlyValue = 40,
            };
        }

        private sealed partial class TestEntity : Entity
        {
            [EntityProperty(EntityPropertyFlag.ClientServer)]
            private int __ClientServerValue;

            [EntityProperty(EntityPropertyFlag.AllClients)]
            private int __AllClientsValue;

            [EntityProperty(EntityPropertyFlag.ServerOnly)]
            private int __ServerOnlyValue;

            [EntityProperty(EntityPropertyFlag.ClientOnly)]
            private int __ClientOnlyValue;
        }
    }
}
