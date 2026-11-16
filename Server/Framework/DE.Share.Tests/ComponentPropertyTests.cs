using DE.Share.Entities;
using DE.Server.Entities;
using Xunit;

namespace DE.Share.Tests
{
    public sealed partial class ComponentPropertyTests
    {
        [Theory]
        [InlineData(EntitySerializationProfile.Owner)]
        [InlineData(EntitySerializationProfile.Observer)]
        [InlineData(EntitySerializationProfile.Migration)]
        public void ComponentProperties_RestoreAcrossSidesBeforeInitialization(EntitySerializationProfile reason)
        {
            var source = new ComponentEntity(new ServerTestTransformComponent(), new OtherComponent());
            source.InitializeEntity();
            ((ServerTestTransformComponent)source.First).Value = 42;
            ((ServerTestTransformComponent)source.First).ServerValue = 12;
            source.Other.Value = 73;
            byte[] snapshot = EntitySerializer.SerializeSnapshot(source, reason);

            var targetComponent = new ClientTestTransformComponent();
            var target = new ComponentEntity(targetComponent, new OtherComponent());
            target.InitializeEntity(snapshot, reason);

            Assert.Equal(source.Guid, target.Guid);
            Assert.Equal(42, targetComponent.Value);
            Assert.Equal(reason == EntitySerializationProfile.Migration ? 12 : 0, targetComponent.ServerValue);
            Assert.Equal(42, targetComponent.ValueAtInitialization);
            Assert.Equal(73, target.Other.Value);
            Assert.Equal(0, targetComponent.AfterChanges);
            targetComponent.Value = 43;
            Assert.Equal(42, targetComponent.BeforeValue);
            Assert.Equal(43, targetComponent.AfterValue);
            Assert.Equal(1, targetComponent.AfterChanges);
            targetComponent.Value = 43;
            Assert.Equal(1, targetComponent.AfterChanges);
        }

        private sealed partial class ComponentEntity : ServerEntity
        {
            public ComponentEntity(ServerEntityComponent first, OtherComponent other)
            {
                First = first;
                Other = other;
            }

            [EntityComponent]
            public ServerEntityComponent First { get; }
            [EntityComponent]
            public OtherComponent Other { get; }
            public override bool IsAllowImmigrate() => false;

            /// <summary>Prepare the supplied fixture components before restoring their properties.</summary>
            protected override void PreInitialize()
            {
                base.PreInitialize();
                AddComponent(First);
                AddComponent(Other);
            }
        }

        private sealed partial class ServerTestTransformComponent : ServerEntityComponent
        {
            [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients)]
            private int __Value;

            [EntityProperty(EntityPropertyFlag.ServerOnly)]
            private int __ServerValue;
        }

        private sealed partial class OtherComponent : ServerEntityComponent
        {
            [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients)]
            private int __Value;
        }

        private sealed partial class ClientTestTransformComponent : ServerEntityComponent
        {
            [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients)]
            private int __Value = -1;

            [EntityProperty(EntityPropertyFlag.ServerOnly)]
            private int __ServerValue;

            public int ValueAtInitialization { get; private set; }
            public int BeforeValue { get; private set; }
            public int AfterValue { get; private set; }
            public int AfterChanges { get; private set; }

            protected override void Initialize()
            {
                ValueAtInitialization = Value;
            }

            partial void OnBeforeValueChange(int value)
            {
                BeforeValue = Value;
            }

            partial void OnAfterValueChange(int value)
            {
                AfterValue = Value;
                AfterChanges++;
            }
        }
    }
}
