using System;
using System.Collections.Generic;
using System.Text;

namespace DE.Share.Utils
{
    public static class GateSelector
    {
        private const string GatePrefix = "Gate";
        private const uint FnvOffsetBasis = 2166136261u;
        private const uint FnvPrime = 16777619u;

        public static string SelectTargetGateServerId(string account, IEnumerable<string> gateServerIds)
        {
            if (string.IsNullOrWhiteSpace(account) || gateServerIds == null)
            {
                return string.Empty;
            }

            List<string> orderedGateServerIds = GetOrderedGateServerIds(gateServerIds);
            if (orderedGateServerIds.Count == 0)
            {
                return string.Empty;
            }

            uint hash = ComputeAccountGateHash(account.Trim());
            int gateIndex = (int)(hash % (uint)orderedGateServerIds.Count);
            return orderedGateServerIds[gateIndex];
        }

        public static uint ComputeAccountGateHash(string account)
        {
            if (string.IsNullOrWhiteSpace(account))
            {
                return 0;
            }

            uint hash = FnvOffsetBasis;
            byte[] accountBytes = Encoding.UTF8.GetBytes(account);
            for (int index = 0; index < accountBytes.Length; index++)
            {
                hash ^= accountBytes[index];
                hash *= FnvPrime;
            }

            return hash;
        }

        public static List<string> GetOrderedGateServerIds(IEnumerable<string> gateServerIds)
        {
            List<string> orderedGateServerIds = new List<string>();
            foreach (string gateServerId in gateServerIds)
            {
                if (string.IsNullOrWhiteSpace(gateServerId))
                {
                    continue;
                }

                orderedGateServerIds.Add(gateServerId.Trim());
            }

            orderedGateServerIds.Sort(CompareGateServerId);
            return orderedGateServerIds;
        }

        private static int CompareGateServerId(string leftServerId, string rightServerId)
        {
            int leftIndex;
            int rightIndex;
            bool hasLeftIndex = TryParseGateIndex(leftServerId, out leftIndex);
            bool hasRightIndex = TryParseGateIndex(rightServerId, out rightIndex);
            if (hasLeftIndex && hasRightIndex && leftIndex != rightIndex)
            {
                return leftIndex.CompareTo(rightIndex);
            }

            return string.CompareOrdinal(leftServerId, rightServerId);
        }

        private static bool TryParseGateIndex(string serverId, out int gateIndex)
        {
            gateIndex = 0;
            if (string.IsNullOrWhiteSpace(serverId)
                || !serverId.StartsWith(GatePrefix, StringComparison.Ordinal)
                || serverId.Length <= GatePrefix.Length)
            {
                return false;
            }

            return int.TryParse(serverId.Substring(GatePrefix.Length), out gateIndex);
        }
    }
}
