using System;
using DE.Share.Rpc;
using Xunit;

namespace DE.Share.Tests
{
    public partial class RpcArrayTests
    {
        [Fact]
        public void GeneratedEntryRpc_RoundTripsEntityStates()
        {
            var target = new EntryTarget();
            var spaceData = new byte[] { 1, 2 };
            var ownerData = new byte[] { 3, 4 };
            var entityTypes = new[] { "ServerNpcEntity", "ServerAvatarEntity" };
            var entityData = new[] { new byte[] { 5, 6 }, Array.Empty<byte>() };
            object[] args = { "ServerSpaceEntity", spaceData, ownerData, entityTypes, entityData };
            uint methodId = RpcMethodId.Compute("OnEnterSpace", args);
            var reader = new RpcBinaryReader(RpcBinaryWriter.SerializeArguments(args));

            Assert.True(EntryTarget.__DEGF_RPC_InvokeClientRpc(target, methodId, reader));

            Assert.Equal("ServerSpaceEntity", target.SpaceType);
            Assert.Equal(spaceData, target.SpaceData);
            Assert.Equal(ownerData, target.OwnerData);
            Assert.Equal(entityTypes, target.EntityTypes);
            Assert.Equal(entityData[0], target.EntityData[0]);
            Assert.Empty(target.EntityData[1]);
            Assert.Equal(0, reader.Remaining);
        }

        [Fact]
        public void EmptyEntityArrays_KeepFollowingArgumentsReadable()
        {
            object[] args = { Array.Empty<string>(), Array.Empty<byte[]>(), 42 };
            var reader = new RpcBinaryReader(RpcBinaryWriter.SerializeArguments(args));

            Assert.Empty(reader.ReadStringArray());
            Assert.Empty(reader.ReadByteArrays());
            Assert.Equal(42, reader.ReadInt32());
            Assert.Equal(0, reader.Remaining);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(int.MaxValue)]
        public void InvalidArrayCount_IsRejectedBeforeAllocation(int count)
        {
            var reader = new RpcBinaryReader(RpcBinaryWriter.SerializeArguments(new object[] { count }));

            Assert.Throws<InvalidOperationException>(() => reader.ReadStringArray());
        }

        private partial class EntryTarget
        {
            public string SpaceType;
            public byte[] SpaceData;
            public byte[] OwnerData;
            public string[] EntityTypes;
            public byte[][] EntityData;

            [ClientRpc]
            public void OnEnterSpace(string spaceType, byte[] spaceData, byte[] ownerData, string[] entityTypes, byte[][] entityData)
            {
                SpaceType = spaceType;
                SpaceData = spaceData;
                OwnerData = ownerData;
                EntityTypes = entityTypes;
                EntityData = entityData;
            }
        }
    }
}
