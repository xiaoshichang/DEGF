using System;
using DE.Server.NativeBridge;
using Xunit;

namespace DE.Server.Tests
{
    public sealed class AvatarMigrationProtocolTests
    {
        [Fact]
        public void RoundTrip_PreservesMigrationEnvelopeAndSnapshot()
        {
            var message = CreateMessage();

            var bytes = AvatarMigrationProtocol.BuildSystemCommandPayload(
                message.TargetGameServerId,
                message
            );

            Assert.True(SystemCommandPayload.TryDeserialize(bytes, 0, bytes.Length, out var commandEnvelope));
            Assert.Equal(SystemCommandKind.AvatarMigration, commandEnvelope.Kind);
            Assert.Equal(message.TargetGameServerId, commandEnvelope.TargetServerId);
            Assert.True(AvatarMigrationProtocol.TryDeserialize(commandEnvelope, out var parsed));
            Assert.Equal(message.Command, parsed.Command);
            Assert.Equal(message.MigrationId, parsed.MigrationId);
            Assert.Equal(message.AvatarId, parsed.AvatarId);
            Assert.Equal(message.TargetSpaceId, parsed.TargetSpaceId);
            Assert.Equal(message.SourceGameServerId, parsed.SourceGameServerId);
            Assert.Equal(message.TargetGameServerId, parsed.TargetGameServerId);
            Assert.Equal(message.GateServerId, parsed.GateServerId);
            Assert.Equal(message.ClientSessionId, parsed.ClientSessionId);
            Assert.Equal(message.AvatarData, parsed.AvatarData);
        }

        [Fact]
        public void TryDeserialize_RejectsCommandThatDoesNotMatchEnvelopeCommandId()
        {
            var message = CreateMessage();
            var bytes = AvatarMigrationProtocol.BuildSystemCommandPayload(
                message.TargetGameServerId,
                message
            );
            Assert.True(SystemCommandPayload.TryDeserialize(bytes, 0, bytes.Length, out var commandEnvelope));
            commandEnvelope.CommandId = (uint)AvatarMigrationCommand.CommitRoute;

            Assert.False(AvatarMigrationProtocol.TryDeserialize(commandEnvelope, out _));
        }

        [Fact]
        public void Response_DoesNotEchoMigrationSnapshot()
        {
            var request = CreateMessage();

            var response = request.CreateResponse(AvatarMigrationCommand.TargetPrepared, true);

            Assert.Empty(response.AvatarData);
            Assert.Equal(request.MigrationId, response.MigrationId);
            Assert.Equal(request.AvatarId, response.AvatarId);
            Assert.Equal(request.TargetSpaceId, response.TargetSpaceId);
        }

        private static AvatarMigrationMessage CreateMessage()
        {
            return new AvatarMigrationMessage
            {
                Command = AvatarMigrationCommand.PrepareTarget,
                MigrationId = new Guid("11111111-2222-3333-4444-555555555555"),
                AvatarId = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                TargetSpaceId = new Guid("12345678-1234-5678-90ab-1234567890ab"),
                SourceGameServerId = "Game1",
                TargetGameServerId = "Game2",
                GateServerId = "Gate1",
                ClientSessionId = 42,
                AvatarData = new byte[] { 1, 3, 5, 7 },
            };
        }
    }
}
