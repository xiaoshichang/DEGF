using System;
using System.Collections.Generic;

namespace DE.Server.Entities
{
    public interface ISpaceAllocationStrategy
    {
        SpaceEntityStubProxy Select(int spaceNo, Guid avatarId, IReadOnlyList<SpaceEntityStubProxy> candidates);
    }

    public sealed class RandomSpaceAllocationStrategy : ISpaceAllocationStrategy
    {
        private readonly Func<int, int> _selectRandomIndex;

        public RandomSpaceAllocationStrategy() : this(new Random().Next)
        {
        }

        public RandomSpaceAllocationStrategy(Func<int, int> selectRandomIndex)
        {
            _selectRandomIndex = selectRandomIndex ?? throw new ArgumentNullException(nameof(selectRandomIndex));
        }

        public SpaceEntityStubProxy Select(int spaceNo, Guid avatarId, IReadOnlyList<SpaceEntityStubProxy> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }
            return candidates[candidates.Count == 1 ? 0 : _selectRandomIndex(candidates.Count)];
        }
    }
}
