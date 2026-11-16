using DE.Share.Entities;
using System;
using Assets.Scripts.DE.Share;
using DE.Server.Entities;
using Xunit;

namespace DE.Share.Tests
{
    public sealed partial class EntitySerializerTests
    {
        [Fact]
        public void OwnerSync_SerializesClientServerAndAllClientsProperties()
        {
            TestEntity source = CreateSource();
            TestEntity target = CreateEntity<TestEntity>(source.Guid);

            byte[] data = EntitySerializer.SerializeSnapshot(source, EntitySerializationProfile.Owner);
            EntitySerializer.ApplySnapshot(target, EntitySerializationProfile.Owner, data);

            Assert.Equal(source.Guid, target.Guid);
            Assert.Equal(source.ClientServerValue, target.ClientServerValue);
            Assert.Equal(source.AllClientsValue, target.AllClientsValue);
            Assert.Equal(default(int), target.ServerOnlyValue);
            Assert.Equal(default(int), target.ClientOnlyValue);
            Assert.Equal(0, target.ClientServerValueChangedCount);
        }

        [Fact]
        public void Broadcase_SerializesOnlyAllClientsProperties()
        {
            TestEntity source = CreateSource();
            TestEntity target = CreateEntity<TestEntity>(source.Guid);

            byte[] data = EntitySerializer.SerializeSnapshot(source, EntitySerializationProfile.Observer);
            EntitySerializer.ApplySnapshot(target, EntitySerializationProfile.Observer, data);

            Assert.Equal(source.Guid, target.Guid);
            Assert.Equal(default(int), target.ClientServerValue);
            Assert.Equal(source.AllClientsValue, target.AllClientsValue);
            Assert.Equal(default(int), target.ServerOnlyValue);
            Assert.Equal(default(int), target.ClientOnlyValue);
        }

        [Fact]
        public void Migrate_SerializesServerOnlyClientServerAndAllClientsProperties()
        {
            TestEntity source = CreateSource();
            TestEntity target = CreateEntity<TestEntity>(source.Guid);

            byte[] data = EntitySerializer.SerializeSnapshot(source, EntitySerializationProfile.Migration);
            EntitySerializer.ApplySnapshot(target, EntitySerializationProfile.Migration, data);

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
            TestEntity target = CreateEntity<TestEntity>(source.Guid);

            byte[] data = EntitySerializer.SerializeSnapshot(source, EntitySerializationProfile.Owner);
            bool result = EntitySerializer.TryDeserialize(target, EntitySerializationProfile.Migration, data);

            Assert.False(result);
        }

        [Fact]
        public void LoginRsp_SerializesAvatarData()
        {
            TestEntity source = CreateSource();
            byte[] avatarData = EntitySerializer.SerializeSnapshot(source, EntitySerializationProfile.Owner);
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

            TestEntity target = CreateEntity<TestEntity>(source.Guid);
            Assert.True(EntitySerializer.TryDeserialize(target, EntitySerializationProfile.Owner, parsedMessage.AvatarData));
            Assert.Equal(source.Guid, target.Guid);
            Assert.Equal(source.ClientServerValue, target.ClientServerValue);
        }

        [Fact]
        public void OwnerSync_SerializesAvatarDisplayProperties()
        {
            TestAvatarEntity source = CreateEntity<TestAvatarEntity>(new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
            source.HeadIcon = "HeadIcon_03";
            source.Score = 6400;
            TestAvatarEntity target = CreateEntity<TestAvatarEntity>(source.Guid);

            byte[] avatarData = EntitySerializer.SerializeSnapshot(source, EntitySerializationProfile.Owner);
            Assert.True(EntitySerializer.TryDeserialize(target, EntitySerializationProfile.Owner, avatarData));

            Assert.Equal(source.Guid, target.Guid);
            Assert.Equal(source.HeadIcon, target.HeadIcon);
            Assert.Equal(source.Score, target.Score);
        }

        private static TestEntity CreateSource()
        {
            TestEntity entity = CreateEntity<TestEntity>(new Guid("11111111-2222-3333-4444-555555555555"));
            entity.ClientServerValue = 10;
            entity.AllClientsValue = 20;
            entity.ServerOnlyValue = 30;
            entity.ClientOnlyValue = 40;
            return entity;
        }

        private static TEntity CreateEntity<TEntity>(Guid? entityId = null) where TEntity : ServerEntity, new()
        {
            var entity = new TEntity();
            entity.InitializeEntity(entityId: entityId);
            return entity;
        }

        private sealed partial class TestEntity : ServerEntity
        {
            public override bool IsAllowImmigrate() => false;

            public TestEntity()
            {
            }
            [EntityProperty(EntityPropertyFlag.ClientServer)]
            private int __ClientServerValue;

            [EntityProperty(EntityPropertyFlag.ClientOnly)]
            private int __ClientOnlyValue;

            [EntityProperty(EntityPropertyFlag.AllClients)]
            private int __AllClientsValue;

            [EntityProperty(EntityPropertyFlag.ServerOnly)]
            private int __ServerOnlyValue;

            public int ClientServerValueChangedCount { get; private set; }

            partial void OnAfterClientServerValueChange(int value)
            {
                ClientServerValueChangedCount++;
            }
        }

        private sealed partial class TestAvatarEntity : ServerEntity
        {
            public override bool IsAllowImmigrate() => false;

            public TestAvatarEntity()
            {
            }

            protected override void PreInitialize()
            {
                base.PreInitialize();
                HeadIcon = string.Empty;
            }
            [EntityProperty(EntityPropertyFlag.ClientServer)]
            private string __HeadIcon;

            [EntityProperty(EntityPropertyFlag.ClientServer)]
            private int __Score;
        }
    }
}
