using System;
using System.Collections.Generic;
using System.Linq;
using DE.Share.Rpc;

namespace DE.Server.Entities
{
    public sealed class SpaceInstance
    {
        public SpaceInstance(Guid spaceId, int spaceNo, string gameServerId, int maxPlayers)
        {
            SpaceId = spaceId;
            SpaceNo = spaceNo;
            MailBox = new EntityMailBox(spaceId, gameServerId);
            MaxPlayers = maxPlayers;
        }

        public Guid SpaceId { get; }
        public int SpaceNo { get; }
        public EntityMailBox MailBox { get; internal set; }
        public int MaxPlayers { get; }
        public bool IsReady { get; internal set; }
        public int AvatarCount { get; internal set; }
        public ulong LoadRevision { get; internal set; }
        public bool HasCapacity => MaxPlayers == 0 || AvatarCount < MaxPlayers;
    }

    public interface ISpaceAllocationStrategy
    {
        SpaceInstance Select(int spaceNo, Guid avatarId, IReadOnlyList<SpaceInstance> candidates);
    }

    public sealed class RandomSpaceAllocationStrategy : ISpaceAllocationStrategy
    {
        private readonly Func<int, int> _next;

        public RandomSpaceAllocationStrategy() : this(new Random().Next)
        {
        }

        public RandomSpaceAllocationStrategy(Func<int, int> next)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
        }

        public SpaceInstance Select(int spaceNo, Guid avatarId, IReadOnlyList<SpaceInstance> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }
            return candidates[candidates.Count == 1 ? 0 : _next(candidates.Count)];
        }
    }

    public sealed class SpaceRegistry
    {
        private readonly Dictionary<Guid, SpaceInstance> _instances = new();
        private readonly Dictionary<int, ISpaceAllocationStrategy> _strategies = new();
        public ISpaceAllocationStrategy DefaultStrategy { get; set; }
        public IReadOnlyCollection<SpaceInstance> Instances => _instances.Values;
        public bool IsReady => _instances.Values.All(instance => instance.IsReady);

        public SpaceRegistry(ISpaceAllocationStrategy defaultStrategy = null)
        {
            DefaultStrategy = defaultStrategy ?? new RandomSpaceAllocationStrategy();
        }

        public SpaceInstance Plan(int spaceNo, string gameServerId, int maxPlayers)
        {
            if (spaceNo <= 0 || maxPlayers < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(spaceNo));
            }
            if (string.IsNullOrWhiteSpace(gameServerId))
            {
                throw new ArgumentException("Space host is required.", nameof(gameServerId));
            }
            var instance = new SpaceInstance(Guid.NewGuid(), spaceNo, gameServerId, maxPlayers);
            _instances.Add(instance.SpaceId, instance);
            return instance;
        }

        public bool Confirm(EntityMailBox mailbox, int spaceNo)
        {
            if (!mailbox.IsValid || !_instances.TryGetValue(mailbox.EntityId, out var instance) || instance.SpaceNo != spaceNo
                || instance.MailBox.BindingGame != mailbox.BindingGame)
            {
                return false;
            }
            instance.MailBox = mailbox;
            instance.IsReady = true;
            return true;
        }

        public bool ReportLoad(Guid spaceId, string gameServerId, int avatarCount, ulong revision)
        {
            if (!_instances.TryGetValue(spaceId, out var instance) || instance.MailBox.BindingGame != gameServerId
                || avatarCount < 0 || revision <= instance.LoadRevision)
            {
                return false;
            }
            instance.AvatarCount = avatarCount;
            instance.LoadRevision = revision;
            return true;
        }

        public void SetStrategy(int spaceNo, ISpaceAllocationStrategy strategy)
        {
            _strategies[spaceNo] = strategy ?? throw new ArgumentNullException(nameof(strategy));
        }

        public SpaceInstance Find(Guid spaceId)
        {
            return _instances.TryGetValue(spaceId, out var instance) && instance.IsReady ? instance : null;
        }

        public SpaceInstance Allocate(int spaceNo, Guid avatarId)
        {
            var candidates = _instances.Values.Where(item => item.SpaceNo == spaceNo && item.IsReady && item.HasCapacity).ToList();
            var strategy = _strategies.TryGetValue(spaceNo, out var configured) ? configured : DefaultStrategy;
            var selected = strategy.Select(spaceNo, avatarId, candidates.AsReadOnly());
            return selected != null && candidates.Contains(selected) ? selected : null;
        }
    }
}
