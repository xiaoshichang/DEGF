using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace DE.Server.NativeBridge
{
    public class ServerStubDistributeTable
    {
        public Dictionary<string, List<string>> NodeToStubTypeKeys { get; set; } = new Dictionary<string, List<string>>();

        public IReadOnlyList<string> GetAssignedStubTypeKeys(string serverId)
        {
            if (string.IsNullOrWhiteSpace(serverId))
            {
                return Array.Empty<string>();
            }

            if (!NodeToStubTypeKeys.TryGetValue(serverId, out var assignedStubTypeKeys) || assignedStubTypeKeys == null)
            {
                return Array.Empty<string>();
            }

            return assignedStubTypeKeys;
        }

        public static ServerStubDistributeTable BuildTable(List<Type> stubTypes, List<string> allGameNode)
        {
            if (stubTypes == null)
            {
                throw new ArgumentNullException(nameof(stubTypes));
            }

            if (allGameNode == null)
            {
                throw new ArgumentNullException(nameof(allGameNode));
            }

            var gameNodes = allGameNode
                .Where(serverId => !string.IsNullOrWhiteSpace(serverId))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(serverId => serverId, StringComparer.Ordinal)
                .ToList();

            if (gameNodes.Count == 0)
            {
                throw new InvalidOperationException("At least one game node is required to build stub distribute table.");
            }

            var table = new ServerStubDistributeTable();
            foreach (var gameNode in gameNodes)
            {
                table.NodeToStubTypeKeys[gameNode] = new List<string>();
            }

            var stubTypeKeys = stubTypes
                .Where(stubType => stubType != null)
                .Select(ManagedRuntimeState.GetStubTypeKey)
                .Where(stubTypeKey => !string.IsNullOrWhiteSpace(stubTypeKey))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(stubTypeKey => stubTypeKey, StringComparer.Ordinal)
                .ToList();

            for (var index = 0; index < stubTypeKeys.Count; ++index)
            {
                var gameNode = gameNodes[index % gameNodes.Count];
                table.NodeToStubTypeKeys[gameNode].Add(stubTypeKeys[index]);
            }

            return table;
        }

        public static byte[] ConvertServerStubDistributeTableToPayload(ServerStubDistributeTable table)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            return JsonSerializer.SerializeToUtf8Bytes(table);
        }

        public static ServerStubDistributeTable ConvertServerStubDistributeTableFromPayload(byte[] payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (payload.Length == 0)
            {
                return new ServerStubDistributeTable();
            }

            var table = JsonSerializer.Deserialize<ServerStubDistributeTable>(payload) ?? new ServerStubDistributeTable();
            if (table.NodeToStubTypeKeys == null)
            {
                table.NodeToStubTypeKeys = new Dictionary<string, List<string>>();
            }

            var normalizedNodeToStubTypeKeys = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var nodeToStubTypeKey in table.NodeToStubTypeKeys)
            {
                if (string.IsNullOrWhiteSpace(nodeToStubTypeKey.Key))
                {
                    continue;
                }

                normalizedNodeToStubTypeKeys[nodeToStubTypeKey.Key] = nodeToStubTypeKey.Value == null
                    ? new List<string>()
                    : nodeToStubTypeKey.Value
                        .Where(stubTypeKey => !string.IsNullOrWhiteSpace(stubTypeKey))
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(stubTypeKey => stubTypeKey, StringComparer.Ordinal)
                        .ToList();
            }

            table.NodeToStubTypeKeys = normalizedNodeToStubTypeKeys;
            return table;
        }
    }
}
