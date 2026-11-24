using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DE.Client.Entities
{
    /// <summary>从业务程序集发现实体类型，并将服务器类型名映射到普通客户端实体。</summary>
    public static class ClientEntityFactory
    {
        private static readonly Dictionary<string, Type> EntityTypes = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            { "ServerAvatarEntity", typeof(ClientAvatarEntity) },
            { "ServerSpaceEntity", typeof(ClientSpaceEntity) },
            { "ServerNpcEntity", typeof(ClientNpcEntity) },
        };

        public static Type AvatarType { get; private set; } = typeof(ClientAvatarEntity);
        public static Type PlayerAvatarType { get; private set; } = typeof(ClientAvatarEntity);
        public static Type SpaceType { get; private set; } = typeof(ClientSpaceEntity);
        public static Type NpcType { get; private set; } = typeof(ClientNpcEntity);

        /// <summary>启动时扫描一次业务程序集；普通 Avatar 的唯一业务派生类型用于创建本人。</summary>
        internal static void Initialize(Assembly gameplayAssembly)
        {
            if (gameplayAssembly == null)
            {
                throw new ArgumentNullException(nameof(gameplayAssembly));
            }

            Reset();
            var entityTypes = gameplayAssembly.GetTypes()
                .Where(type => typeof(ClientEntity).IsAssignableFrom(type) && !type.IsAbstract && !type.ContainsGenericParameters)
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();
            AvatarType = FindGameplayType(typeof(ClientAvatarEntity), entityTypes);
            PlayerAvatarType = FindGameplayType(AvatarType, entityTypes);
            SpaceType = FindGameplayType(typeof(ClientSpaceEntity), entityTypes);
            NpcType = FindGameplayType(typeof(ClientNpcEntity), entityTypes);

            ValidateConstructor(PlayerAvatarType);
            foreach (var entityType in entityTypes)
            {
                // 本人类型只能由登录流程创建，服务器 Avatar 的观察者映射始终使用普通 Avatar。
                if (PlayerAvatarType != AvatarType && PlayerAvatarType.IsAssignableFrom(entityType))
                {
                    continue;
                }
                ValidateConstructor(entityType);
                string serverTypeName = entityType.Name.Replace("Client", "Server");
                if (EntityTypes.ContainsKey(serverTypeName))
                {
                    throw new InvalidOperationException("Duplicate client entity mapping for server type: " + serverTypeName);
                }
                EntityTypes.Add(serverTypeName, entityType);
            }
        }

        internal static void Reset()
        {
            AvatarType = typeof(ClientAvatarEntity);
            PlayerAvatarType = typeof(ClientAvatarEntity);
            SpaceType = typeof(ClientSpaceEntity);
            NpcType = typeof(ClientNpcEntity);
            EntityTypes.Clear();
            EntityTypes.Add("ServerAvatarEntity", AvatarType);
            EntityTypes.Add("ServerSpaceEntity", SpaceType);
            EntityTypes.Add("ServerNpcEntity", NpcType);
        }

        public static ClientEntity Create(string serverTypeName, byte[] data)
        {
            if (!EntityTypes.TryGetValue(serverTypeName, out var entityType))
            {
                throw new InvalidOperationException("Unregistered server entity type: " + serverTypeName);
            }
            var entity = (ClientEntity)Activator.CreateInstance(entityType);
            entity.InitializeEntity(data, EntitySerializationProfile.Observer);
            return entity;
        }

        private static Type FindGameplayType(Type baseType, IReadOnlyCollection<Type> entityTypes)
        {
            var candidates = entityTypes.Where(type => type != baseType && baseType.IsAssignableFrom(type)).ToArray();
            var roots = candidates.Where(type => !candidates.Any(other => other != type && other.IsAssignableFrom(type))).ToArray();
            if (roots.Length == 0)
            {
                return baseType;
            }
            if (roots.Length != 1)
            {
                throw new InvalidOperationException("Expected one gameplay entity type derived from " + baseType.FullName
                    + ", but found: " + string.Join(", ", roots.Select(type => type.FullName)));
            }
            return roots[0];
        }

        private static void ValidateConstructor(Type entityType)
        {
            if (entityType.GetConstructor(Type.EmptyTypes) == null)
            {
                throw new InvalidOperationException("Client entity must have a public parameterless constructor: " + entityType.FullName);
            }
        }
    }
}
