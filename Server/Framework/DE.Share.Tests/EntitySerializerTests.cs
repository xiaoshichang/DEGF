using System;
using Assets.Scripts.DE.Share;
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

        [Fact]
        public void LoginRsp_SerializesAvatarData()
        {
            TestEntity source = CreateSource();
            byte[] avatarData = EntitySerializer.Serialize(source, EntitySerializeReason.OwnerSync);
            MessageDef.LoginRsp sourceMessage = new MessageDef.LoginRsp
            {
                Version = MessageDef.LoginRsp.CurrentVersion,
                IsSuccess = true,
                StatusCode = 200,
                AvatarId = source.Guid,
                AvatarData = avatarData,
                Error = string.Empty,
            };

            byte[] data = sourceMessage.Serialize();
            bool result = MessageDef.LoginRsp.TryDeserialize(data, 0, data.Length, out MessageDef.LoginRsp parsedMessage);

            Assert.True(result);
            Assert.Equal(source.Guid, parsedMessage.AvatarId);
            Assert.Equal(avatarData, parsedMessage.AvatarData);

            TestEntity target = new TestEntity();
            Assert.True(EntitySerializer.TryDeserialize(target, EntitySerializeReason.OwnerSync, parsedMessage.AvatarData));
            Assert.Equal(source.Guid, target.Guid);
            Assert.Equal(source.ClientServerValue, target.ClientServerValue);
        }

        [Fact]
        public void OwnerSync_SerializesAvatarDisplayProperties()
        {
            TestAvatarEntity source = new TestAvatarEntity
            {
                Guid = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                HeadIcon = "HeadIcon_03",
                Score = 6400,
            };
            TestAvatarEntity target = new TestAvatarEntity();

            byte[] avatarData = EntitySerializer.Serialize(source, EntitySerializeReason.OwnerSync);
            Assert.True(EntitySerializer.TryDeserialize(target, EntitySerializeReason.OwnerSync, avatarData));

            Assert.Equal(source.Guid, target.Guid);
            Assert.Equal(source.HeadIcon, target.HeadIcon);
            Assert.Equal(source.Score, target.Score);
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

        private sealed partial class TestAvatarEntity : Entity
        {
            [EntityProperty(EntityPropertyFlag.ClientServer)]
            private string __HeadIcon = "";

            [EntityProperty(EntityPropertyFlag.ClientServer)]
            private int __Score;
        }
    }
}
