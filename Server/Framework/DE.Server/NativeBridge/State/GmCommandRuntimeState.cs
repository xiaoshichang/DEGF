using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace DE.Server.NativeBridge
{
    public sealed class GmTotalEntityCountRsp
    {
        public ulong RequestId { get; set; }
        public string ServerId { get; set; } = string.Empty;
        public int EntityCount { get; set; }
        public int AvatarCount { get; set; }
        public int StubCount { get; set; }
        public int TotalEntityObjectCount { get; set; }
    }

    public sealed class GmTelnetCommandResult
    {
        public ulong RequestId { get; set; }
        public string Response { get; set; } = string.Empty;
    }

    public sealed class GmCommandRuntimeState
    {
        private static readonly JsonSerializerOptions s_jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        private readonly ManagedRuntimeState _managedRuntimeState;
        private readonly Dictionary<ulong, PendingTotalEntityCountCommand> _pendingTotalEntityCountCommands = new Dictionary<ulong, PendingTotalEntityCountCommand>();

        public GmCommandRuntimeState(ManagedRuntimeState managedRuntimeState)
        {
            _managedRuntimeState = managedRuntimeState ?? throw new ArgumentNullException(nameof(managedRuntimeState));
        }

        public void BeginTotalEntityCountCommand(ulong requestId, IReadOnlyCollection<string> gameServerIds)
        {
            if (requestId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(requestId));
            }

            if (gameServerIds == null || gameServerIds.Count == 0)
            {
                throw new ArgumentException("Game server ids are empty.", nameof(gameServerIds));
            }

            _pendingTotalEntityCountCommands[requestId] = new PendingTotalEntityCountCommand
            {
                RequestId = requestId,
                WaitingGameServerIds = new HashSet<string>(
                    gameServerIds.Where(serverId => !string.IsNullOrWhiteSpace(serverId)),
                    StringComparer.Ordinal
                ),
            };
        }

        public bool CancelCommand(ulong requestId)
        {
            return _pendingTotalEntityCountCommands.Remove(requestId);
        }

        public GmTelnetCommandResult HandleTotalEntityCountRsp(string sourceServerId, byte[] payload)
        {
            var response = JsonSerializer.Deserialize<GmTotalEntityCountRsp>(payload, s_jsonSerializerOptions);
            if (response == null || response.RequestId == 0)
            {
                throw new InvalidOperationException("Invalid TotalEntityCount response payload.");
            }

            if (!_pendingTotalEntityCountCommands.TryGetValue(response.RequestId, out var command))
            {
                DELogger.Warn(nameof(GmCommandRuntimeState), $"TotalEntityCount command not found, requestId={response.RequestId}.");
                return null;
            }

            var serverId = string.IsNullOrWhiteSpace(response.ServerId) ? sourceServerId : response.ServerId;
            response.ServerId = serverId;
            command.Responses[serverId] = response;
            command.WaitingGameServerIds.Remove(serverId);

            if (command.WaitingGameServerIds.Count > 0)
            {
                return null;
            }

            _pendingTotalEntityCountCommands.Remove(response.RequestId);
            return new GmTelnetCommandResult
            {
                RequestId = response.RequestId,
                Response = BuildTotalEntityCountText(command),
            };
        }

        public void Uninitialize()
        {
            _pendingTotalEntityCountCommands.Clear();
        }

        private static string BuildTotalEntityCountText(PendingTotalEntityCountCommand command)
        {
            var responses = command.Responses
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Value)
                .ToList();

            var totalEntityCount = responses.Sum(response => response.EntityCount);
            var totalAvatarCount = responses.Sum(response => response.AvatarCount);
            var totalStubCount = responses.Sum(response => response.StubCount);
            var totalEntityObjectCount = responses.Sum(response => response.TotalEntityObjectCount);

            var builder = new StringBuilder();
            builder.Append("TotalEntityCount requestId=").Append(command.RequestId).Append("\r\n");
            foreach (var response in responses)
            {
                builder
                    .Append(response.ServerId).Append(":\r\n")
                    .Append("  entityCount: ").Append(response.EntityCount).Append("\r\n")
                    .Append("  avatarCount: ").Append(response.AvatarCount).Append("\r\n")
                    .Append("  stubCount: ").Append(response.StubCount).Append("\r\n")
                    .Append("  totalEntityObjectCount: ").Append(response.TotalEntityObjectCount).Append("\r\n");
            }

            builder
                .Append("Total:\r\n")
                .Append("  entityCount: ").Append(totalEntityCount).Append("\r\n")
                .Append("  avatarCount: ").Append(totalAvatarCount).Append("\r\n")
                .Append("  stubCount: ").Append(totalStubCount).Append("\r\n")
                .Append("  totalEntityObjectCount: ").Append(totalEntityObjectCount);
            return builder.ToString();
        }

        private sealed class PendingTotalEntityCountCommand
        {
            public ulong RequestId { get; set; }
            public HashSet<string> WaitingGameServerIds { get; set; } = new HashSet<string>(StringComparer.Ordinal);
            public Dictionary<string, GmTotalEntityCountRsp> Responses { get; } = new Dictionary<string, GmTotalEntityCountRsp>(StringComparer.Ordinal);
        }
    }
}
