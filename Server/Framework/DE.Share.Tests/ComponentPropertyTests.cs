using System.Collections.Generic;
using DE.Share.Entities;
using Xunit;

namespace DE.Share.Tests
{
    public sealed partial class ComponentPropertyTests
    {
        [Theory]
        [InlineData(EntitySerializeReason.OwnerSync)]
        [InlineData(EntitySerializeReason.Broadcase)]
        [InlineData(EntitySerializeReason.Migrate)]
        public void ComponentProperties_RestoreAcrossSidesBeforeInitialization(EntitySerializeReason reason)
        {
            var source = new ComponentEntity(new ServerTestTransformComponent(), new OtherComponent());
            source.InitializeEntity();
            ((ServerTestTransformComponent)source.First).Value = 42;
            ((ServerTestTransformComponent)source.First).ServerValue = 12;
            source.Other.Value = 73;
            byte[] snapshot = EntitySerializer.Serialize(source, reason);

            var targetComponent = new ClientTestTransformComponent();
            var target = new ComponentEntity(targetComponent, new OtherComponent());
            target.InitializeEntity(snapshot, reason);

            Assert.Equal(source.Guid, target.Guid);
            Assert.Equal(42, targetComponent.Value);
            Assert.Equal(reason == EntitySerializeReason.Migrate ? 12 : 0, targetComponent.ServerValue);
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

        private sealed class ComponentEntity : Entity, IEntityComponentProvider
        {
            private readonly EntityComponentCollection<EntityComponent> _components = new EntityComponentCollection<EntityComponent>();

            public ComponentEntity(EntityComponent first, OtherComponent other)
            {
                First = first;
                Other = other;
            }

            public EntityComponent First { get; }
            public OtherComponent Other { get; }
            public IEnumerable<EntityComponent> PropertyComponents => _components.Items;
            protected override void PreInitialize()
            {
                base.PreInitialize();
                _components.Add(this, First);
                _components.Add(this, Other);
            }
            protected override void PostInitialize()
            {
                base.PostInitialize();
                _components.PostInitialize();
            }
            protected override void PreUnInitialize()
            {
                _components.PreUnInitialize();
                base.PreUnInitialize();
            }
            protected override void PostUnInitialize()
            {
                _components.PostUnInitialize();
                base.PostUnInitialize();
            }
            protected override void Initialize()
            {
                base.Initialize();
                _components.Initialize();
            }
            protected override void UnInitialize()
            {
                _components.UnInitialize();
                base.UnInitialize();
            }
        }

        private sealed partial class ServerTestTransformComponent : EntityComponent
        {
            [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients)]
            private int __Value;

            [EntityProperty(EntityPropertyFlag.ServerOnly)]
            private int __ServerValue;
        }

        private sealed partial class OtherComponent : EntityComponent
        {
            [EntityProperty(EntityPropertyFlag.ClientServer | EntityPropertyFlag.AllClients)]
            private int __Value;
        }

        private sealed partial class ClientTestTransformComponent : EntityComponent
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
