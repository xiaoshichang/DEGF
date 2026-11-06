using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DE.Server.NativeBridge;

namespace DE.Server.Entities
{
    internal sealed class CreateEntityCommand
    {
        public string OriginServerId { get; set; }
        public string EntityAssembly { get; set; }
        public string EntityType { get; set; }
        public string[] ParameterTypes { get; set; }
        public JsonElement[] ConstructorArguments { get; set; }
    }

    public static class RemoteEntityCaller
    {
        internal static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions { IncludeFields = true };

        /// <summary>Sends a creation request. Constructor arguments must be JSON-serializable data, not live runtime objects.</summary>
        public static bool Create(string targetServerId, Type entityType, Guid entityId, params object[] constructorArguments)
        {
            var runtime = ManagedRuntimeState.RequireCurrent();
            if (entityId == Guid.Empty || !runtime.GetGameServerIds().Contains(targetServerId))
            {
                return false;
            }
            ValidateEntityType(entityType);
            object[] arguments = constructorArguments ?? Array.Empty<object>();
            ConstructorInfo constructor = FindConstructor(entityType, arguments);
            var parameters = constructor.GetParameters();
            var serializedArguments = new JsonElement[arguments.Length];
            for (int index = 0; index < arguments.Length; index++)
            {
                serializedArguments[index] = JsonSerializer.SerializeToElement(arguments[index], parameters[index].ParameterType, SerializerOptions);
            }
            var request = new CreateEntityCommand
            {
                OriginServerId = runtime.ServerId,
                EntityAssembly = entityType.Assembly.GetName().Name,
                EntityType = entityType.FullName,
                ParameterTypes = parameters.Select(parameter => parameter.ParameterType.AssemblyQualifiedName).ToArray(),
                ConstructorArguments = serializedArguments,
            };
            return SystemCommandCaller.Send(new SystemCommandPayload
            {
                Version = SystemCommandPayload.CurrentVersion,
                Kind = SystemCommandKind.CreateEntity,
                EntityId = entityId,
                TargetServerId = targetServerId,
                Data = JsonSerializer.SerializeToUtf8Bytes(request),
            });
        }

        internal static void ValidateEntityType(Type entityType)
        {
            if (entityType == null || entityType.IsAbstract || entityType.ContainsGenericParameters
                || !typeof(ServerEntity).IsAssignableFrom(entityType) || typeof(ServerStubEntity).IsAssignableFrom(entityType))
            {
                throw new ArgumentException("Remote creation requires a concrete ServerEntity type other than a Stub.", nameof(entityType));
            }
        }

        internal static bool TryReadCommand(SystemCommandPayload command, out CreateEntityCommand request)
        {
            request = null;
            if (command.Kind != SystemCommandKind.CreateEntity || command.EntityId == Guid.Empty || command.CommandId != 0 || command.Data == null)
            {
                return false;
            }
            try
            {
                request = JsonSerializer.Deserialize<CreateEntityCommand>(command.Data);
                return request != null && !string.IsNullOrWhiteSpace(request.OriginServerId)
                    && !string.IsNullOrWhiteSpace(request.EntityAssembly) && !string.IsNullOrWhiteSpace(request.EntityType)
                    && request.ParameterTypes != null && request.ConstructorArguments != null
                    && request.ParameterTypes.Length == request.ConstructorArguments.Length
                    && request.ParameterTypes.All(type => !string.IsNullOrWhiteSpace(type));
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static ConstructorInfo FindConstructor(Type entityType, object[] arguments)
        {
            var candidates = entityType.GetConstructors().Where(constructor =>
            {
                var parameters = constructor.GetParameters();
                return parameters.Length == arguments.Length && parameters.Select((parameter, index) =>
                    arguments[index] == null
                        ? !parameter.ParameterType.IsValueType || Nullable.GetUnderlyingType(parameter.ParameterType) != null
                        : parameter.ParameterType.IsInstanceOfType(arguments[index])).All(matches => matches);
            }).Select(constructor => new
            {
                Constructor = constructor,
                ExactMatches = constructor.GetParameters().Where((parameter, index) => parameter.ParameterType == arguments[index]?.GetType()).Count(),
            }).OrderByDescending(candidate => candidate.ExactMatches).ToArray();
            if (candidates.Length == 0 || (candidates.Length > 1 && candidates[0].ExactMatches == candidates[1].ExactMatches))
            {
                throw new ArgumentException("Constructor arguments must identify exactly one public entity constructor.", nameof(arguments));
            }
            return candidates[0].Constructor;
        }
    }
}
