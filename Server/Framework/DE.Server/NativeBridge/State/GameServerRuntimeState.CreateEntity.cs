using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DE.Server.Entities;

namespace DE.Server.NativeBridge
{
    public sealed partial class GameServerRuntimeState
    {
        private readonly Dictionary<Guid, RemoteEntityCreation> _remoteEntityCreations = new();

        private bool HandleCreateEntityCommand(string sourceServerId, SystemCommandPayload command)
        {
            if (!RemoteEntityCaller.TryReadCommand(command, out var request)
                || (!_managedRuntimeState.GetGameServerIds().Contains(request.OriginServerId)
                    && !_managedRuntimeState.GetGateServerIds().Contains(request.OriginServerId))
                || (sourceServerId == _managedRuntimeState.ServerId && request.OriginServerId != sourceServerId))
            {
                return false;
            }
            ServerEntity entity = null;
            try
            {
                byte[] fingerprint = SHA256.HashData(command.Data);
                if (Entities.TryGetValue(command.EntityId, out var existing))
                {
                    if (_remoteEntityCreations.TryGetValue(command.EntityId, out var previous)
                        && ReferenceEquals(previous.Entity, existing) && previous.Fingerprint.SequenceEqual(fingerprint))
                    {
                        if (existing is ServerSpaceEntity existingSpace)
                        {
                            existingSpace.CompleteCreation();
                        }
                        return true;
                    }
                    throw new InvalidOperationException("The requested entity ID is already in use by a different creation.");
                }
                Type entityType = ResolveRemoteEntityType(request);
                RemoteEntityCaller.ValidateEntityType(entityType);
                var constructor = entityType.GetConstructors().SingleOrDefault(candidate => candidate.GetParameters()
                    .Select(parameter => parameter.ParameterType.AssemblyQualifiedName).SequenceEqual(request.ParameterTypes));
                if (constructor == null)
                {
                    throw new InvalidOperationException("The requested public entity constructor does not exist.");
                }
                var parameters = constructor.GetParameters();
                var arguments = new object[parameters.Length];
                for (int index = 0; index < parameters.Length; index++)
                {
                    arguments[index] = JsonSerializer.Deserialize(request.ConstructorArguments[index].GetRawText(),
                        parameters[index].ParameterType, RemoteEntityCaller.SerializerOptions);
                }
                entity = (ServerEntity)constructor.Invoke(arguments);
                entity.InitializeEntity(entityId: command.EntityId);
                if (entity.Guid != command.EntityId)
                {
                    throw new InvalidOperationException("Entity initialization changed the requested entity ID.");
                }
                _remoteEntityCreations.Add(entity.Guid, new RemoteEntityCreation { Entity = entity, Fingerprint = fingerprint });
                RegisterLocalEntity(entity);
                if (entity is ServerSpaceEntity space)
                {
                    space.CompleteCreation();
                }
                return true;
            }
            catch (Exception exception)
            {
                if (entity != null)
                {
                    if (_remoteEntityCreations.TryGetValue(entity.Guid, out var creation) && ReferenceEquals(creation.Entity, entity))
                    {
                        _remoteEntityCreations.Remove(entity.Guid);
                    }
                    if (Entities.TryGetValue(entity.Guid, out var registered) && ReferenceEquals(registered, entity))
                    {
                        UnregisterLocalEntity(entity);
                    }
                    else
                    {
                        entity.UnInitializeEntity();
                    }
                }
                DELogger.Error(nameof(GameServerRuntimeState),
                    $"Remote entity creation failed, entityId={command.EntityId}, type={request.EntityType}: {exception.GetBaseException()}");
                return false;
            }
        }

        private Type ResolveRemoteEntityType(CreateEntityCommand request)
        {
            var assemblies = new List<Assembly> { typeof(ServerEntity).Assembly };
            if (_managedRuntimeState.GameplayAssembly != null)
            {
                assemblies.Add(_managedRuntimeState.GameplayAssembly);
            }
            assemblies.AddRange(_managedRuntimeState.PreloadedAssemblies);
            var candidates = assemblies.Distinct().Where(assembly => assembly.GetName().Name == request.EntityAssembly)
                .Select(assembly => assembly.GetType(request.EntityType, false, false)).Where(type => type != null).Distinct().ToArray();
            if (candidates.Length != 1)
            {
                throw new InvalidOperationException("The requested entity type must exist in exactly one configured assembly.");
            }
            return candidates[0];
        }

        private sealed class RemoteEntityCreation
        {
            public ServerEntity Entity;
            public byte[] Fingerprint;
        }
    }
}
