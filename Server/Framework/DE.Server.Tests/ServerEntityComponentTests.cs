using System;
using System.Collections.Generic;
using DE.Server.Entities;
using DE.Share.Entities;
using Xunit;

namespace DE.Server.Tests
{
    public class ServerEntityComponentTests
    {
        [Fact]
        public void Lifecycle_InitializesTypedComponentsAfterRestoringEntityIdentity()
        {
            var order = new List<string>();
            var entity = new TestEntity(order);
            var id = Guid.NewGuid();

            entity.InitializeEntity(entityId: id);

            Assert.Equal(id, entity.Component.InitializedOwnerId);
            Assert.Same(entity.Component, entity.GetComponent<TestComponent>());
            Assert.Equal(new[] { "entity.initialize", "component.initialize", "entity.post" }, order);

            entity.UnInitializeEntity();
            entity.UnInitializeEntity();

            Assert.Equal(new[] { "entity.initialize", "component.initialize", "entity.post", "entity.uninitialize", "component.uninitialize" }, order);
            Assert.Null(entity.Component.OwnerEntity);
            Assert.Equal(EntityLifecycleState.UnInitialized, entity.Component.LifecycleState);
        }

        private sealed class TestEntity : ServerEntity
        {
            private readonly List<string> _order;

            public TestEntity(List<string> order)
            {
                _order = order;
                Component = AddComponent(new TestComponent(order));
            }

            public TestComponent Component { get; }

            protected override void Initialize()
            {
                _order.Add("entity.initialize");
                base.Initialize();
            }

            protected override void PostInitialize()
            {
                _order.Add("entity.post");
                base.PostInitialize();
            }

            protected override void UnInitialize()
            {
                _order.Add("entity.uninitialize");
                base.UnInitialize();
            }

            public override bool IsAllowImmigrate()
            {
                return false;
            }
        }

        private sealed class TestComponent : ServerEntityComponent
        {
            private readonly List<string> _order;

            public TestComponent(List<string> order)
            {
                _order = order;
            }

            public Guid InitializedOwnerId { get; private set; }

            protected override void Initialize()
            {
                InitializedOwnerId = OwnerEntity.Guid;
                _order.Add("component.initialize");
                base.Initialize();
            }

            protected override void UnInitialize()
            {
                _order.Add("component.uninitialize");
                base.UnInitialize();
            }
        }
    }
}
