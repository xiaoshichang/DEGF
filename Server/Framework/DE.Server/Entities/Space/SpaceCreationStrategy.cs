using System.Collections.Generic;

namespace DE.Server.Entities
{
    public interface ISpaceCreationStrategy
    {
        string SelectGameServer(int spaceNo, int creationIndex, IReadOnlyList<string> gameServerIds);
    }

    public sealed class RoundRobinSpaceCreationStrategy : ISpaceCreationStrategy
    {
        public string SelectGameServer(int spaceNo, int creationIndex, IReadOnlyList<string> gameServerIds)
        {
            if (gameServerIds == null || gameServerIds.Count == 0)
            {
                return string.Empty;
            }
            return gameServerIds[creationIndex % gameServerIds.Count];
        }
    }
}
