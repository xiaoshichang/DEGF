using System;
using System.Collections.Generic;
using System.Text.Json;
using DE.Server.Entities;
using DE.Server.NativeBridge;
using DE.Share.Entities;
using DE.Share.Rpc;

namespace DE.Server.Entities
{
    public enum AvatarImmigrationState
    {
        Idle = 0,
        FreezingRoute = 1,
        Serializing = 2,
        PreparingTarget = 3,
        TargetPrepared = 4,
        ActivatingTarget = 5,
        TargetActivated = 6,
        CommittingRoute = 7,
        CompletingTarget = 8,
        Completed = 9,
        RollingBack = 10,
        Failed = 11,
    }

    public partial class AvatarEntity
    {
        public AvatarImmigrationState ImmigrationState { get; private set; } = AvatarImmigrationState.Idle;
        public Guid ImmigrationId { get; private set; }
        public EntityMailBox ImmigrationTargetSpace { get; private set; }

        public override bool IsAllowImmigrate()
        {
            return true;
        }


        internal void SetImmigrationState(
            AvatarImmigrationState state,
            Guid migrationId,
            EntityMailBox targetSpace,
            string sourceGameServerId,
            string targetGameServerId)
        {
            if (!IsValidImmigrationStateTransition(ImmigrationState, state))
            {
                var error = $"Invalid Avatar immigration state transition, avatarId={Guid}, migrationId={migrationId}, currentState={ImmigrationState}, targetState={state}.";
                DELogger.Error(nameof(AvatarEntity), error);
                throw new InvalidOperationException(error);
            }

            if (ImmigrationState != AvatarImmigrationState.Idle)
            {
                if (ImmigrationId != migrationId)
                {
                    var error = $"Avatar immigration state transition rejected because migration id changed, avatarId={Guid}, currentMigrationId={ImmigrationId}, incomingMigrationId={migrationId}, currentState={ImmigrationState}, targetState={state}.";
                    DELogger.Error(nameof(AvatarEntity), error);
                    throw new InvalidOperationException(error);
                }

                if (ImmigrationTargetSpace.EntityId != targetSpace.EntityId)
                {
                    var error = $"Avatar immigration state transition rejected because target Space id changed, avatarId={Guid}, migrationId={migrationId}, currentTargetSpaceId={ImmigrationTargetSpace.EntityId}, incomingTargetSpaceId={targetSpace.EntityId}.";
                    DELogger.Error(nameof(AvatarEntity), error);
                    throw new InvalidOperationException(error);
                }

                if (!string.Equals(ImmigrationTargetSpace.BindingGame, targetSpace.BindingGame, StringComparison.Ordinal))
                {
                    var error = $"Avatar immigration state transition rejected because target Game id changed, avatarId={Guid}, migrationId={migrationId}, currentTargetGameServerId={ImmigrationTargetSpace.BindingGame}, incomingTargetGameServerId={targetSpace.BindingGame}.";
                    DELogger.Error(nameof(AvatarEntity), error);
                    throw new InvalidOperationException(error);
                }
            }

            ImmigrationState = state;
            ImmigrationId = migrationId;
            ImmigrationTargetSpace = targetSpace;
            DELogger.Info(
                nameof(AvatarEntity),
                $"Avatar immigration state changed, avatarId={Guid}, migrationId={migrationId}, state={state}, sourceGameServerId={sourceGameServerId}, targetGameServerId={targetGameServerId}, targetSpaceId={targetSpace.EntityId}."
            );
        }

        private static bool IsValidImmigrationStateTransition(
            AvatarImmigrationState currentState,
            AvatarImmigrationState targetState)
        {
            AvatarImmigrationState nextState;
            switch (currentState)
            {
            case AvatarImmigrationState.Idle:
                nextState = AvatarImmigrationState.FreezingRoute;
                break;
            case AvatarImmigrationState.FreezingRoute:
                nextState = AvatarImmigrationState.Serializing;
                break;
            case AvatarImmigrationState.Serializing:
                nextState = AvatarImmigrationState.PreparingTarget;
                break;
            case AvatarImmigrationState.PreparingTarget:
                nextState = AvatarImmigrationState.TargetPrepared;
                break;
            case AvatarImmigrationState.TargetPrepared:
                nextState = AvatarImmigrationState.ActivatingTarget;
                break;
            case AvatarImmigrationState.ActivatingTarget:
                nextState = AvatarImmigrationState.TargetActivated;
                break;
            case AvatarImmigrationState.TargetActivated:
                nextState = AvatarImmigrationState.CommittingRoute;
                break;
            case AvatarImmigrationState.CommittingRoute:
                nextState = AvatarImmigrationState.CompletingTarget;
                break;
            case AvatarImmigrationState.CompletingTarget:
                nextState = AvatarImmigrationState.Completed;
                break;
            case AvatarImmigrationState.RollingBack:
                nextState = AvatarImmigrationState.Failed;
                break;
            default:
                return false;
            }

            if (targetState == nextState)
            {
                return true;
            }

            switch (currentState)
            {
            case AvatarImmigrationState.Idle:
                return targetState == AvatarImmigrationState.TargetPrepared;
            case AvatarImmigrationState.FreezingRoute:
                return targetState == AvatarImmigrationState.Failed;
            case AvatarImmigrationState.Serializing:
            case AvatarImmigrationState.PreparingTarget:
            case AvatarImmigrationState.ActivatingTarget:
            case AvatarImmigrationState.CommittingRoute:
                return targetState == AvatarImmigrationState.RollingBack;
            case AvatarImmigrationState.TargetPrepared:
                if (targetState == AvatarImmigrationState.TargetActivated)
                {
                    return true;
                }

                return targetState == AvatarImmigrationState.Failed;
            case AvatarImmigrationState.TargetActivated:
                if (targetState == AvatarImmigrationState.Completed)
                {
                    return true;
                }

                return targetState == AvatarImmigrationState.Failed;
            default:
                return false;
            }
        }

    }
}

namespace DE.Server.NativeBridge
{
    /// <summary>
    /// Avatar 迁移协议命令。
    /// Source Game 是迁移开始时承载 Avatar 的 Game，Target Game 是目标 Space 所绑定的 Game，
    /// Owner Gate 是 Avatar Proxy 所绑定并负责冻结、切换路由和缓存迁移期间消息的 Gate。
    /// Game 之间不直连，因此 Source Game 与 Target Game 之间的命令和响应均由 Owner Gate 转发。
    /// </summary>
    internal enum AvatarMigrationCommand : ushort
    {
        /// <summary>
        /// 含义：请求 Owner Gate 冻结 Avatar 当前路由，迁移期间相关 RPC 改为进入 Gate 缓存队列。
        /// 方向：Source Game → Owner Gate。
        /// 状态影响：Source Game 发送前进入 FreezingRoute；Gate 成功处理后创建 Gate 迁移上下文并保持原路由不变。
        /// </summary>
        FreezeRoute = 1,

        /// <summary>
        /// 含义：Owner Gate 通知 Source Game，Avatar 路由已经冻结。
        /// 方向：Owner Gate → Source Game。
        /// 状态影响：成功时 Source Game 依次进入 Serializing 和 PreparingTarget，Avatar 离开原 Space 并生成迁移快照；失败时迁移进入 Failed。
        /// </summary>
        RouteFrozen = 2,

        /// <summary>
        /// 含义：把 Avatar 迁移快照发送到 Target Game，要求创建尚未激活的目标 Avatar。
        /// 方向：Source Game → Owner Gate → Target Game。
        /// 状态影响：Source Game 保持 PreparingTarget；Target Game 成功反序列化后把目标 Avatar 置为 TargetPrepared，但暂不注册到本地实体表和目标 Space。
        /// </summary>
        PrepareTarget = 3,

        /// <summary>
        /// 含义：Target Game 返回目标 Avatar 的准备结果。
        /// 方向：Target Game → Owner Gate → Source Game。
        /// 状态影响：成功时 Source Game 记录目标已准备，依次进入 TargetPrepared 和 ActivatingTarget；失败时进入 RollingBack。
        /// </summary>
        TargetPrepared = 4,

        /// <summary>
        /// 含义：请求 Target Game 激活已经反序列化的 Avatar。
        /// 方向：Source Game → Owner Gate → Target Game。
        /// 状态影响：成功时 Target Game 注册 Avatar、恢复 Client Session 绑定、进入目标 Space，并把目标 Avatar 置为 TargetActivated。
        /// </summary>
        ActivateTarget = 5,

        /// <summary>
        /// 含义：Target Game 返回目标 Avatar 的激活结果。
        /// 方向：Target Game → Owner Gate → Source Game。
        /// 状态影响：成功时 Source Game 依次进入 TargetActivated 和 CommittingRoute；失败时进入 RollingBack。
        /// </summary>
        TargetActivated = 6,

        /// <summary>
        /// 含义：目标 Avatar 已可服务，请求 Owner Gate 将正式路由从 Source Game 切换到 Target Game。
        /// 方向：Source Game → Owner Gate。
        /// 状态影响：Gate 先把冻结期间缓存的消息按新路由发送，再更新账号路由到 Target Game，记录迁移已提交并解除冻结。
        /// </summary>
        CommitRoute = 7,

        /// <summary>
        /// 含义：Owner Gate 返回 Avatar 路由提交结果。
        /// 方向：Owner Gate → Source Game。
        /// 状态影响：成功时 Source Game 注销原 Avatar，进入 CompletingTarget；失败时进入 RollingBack。
        /// </summary>
        RouteCommitted = 8,

        /// <summary>
        /// 含义：路由已经切换，请求 Target Game 完成迁移并清理目标迁移上下文。
        /// 方向：Source Game → Owner Gate → Target Game。
        /// 状态影响：Target Game 把目标 Avatar 置为 Completed，记录完成的迁移编号并移除目标迁移上下文；Avatar 保持注册且留在目标 Space。
        /// </summary>
        CompleteTarget = 9,

        /// <summary>
        /// 含义：Target Game 返回迁移完成结果。
        /// 方向：Target Game → Owner Gate → Source Game。
        /// 状态影响：成功时 Source Game 把迁移置为 Completed 并移除源迁移上下文；失败时保持 CompletingTarget，并记录错误等待外部处理。
        /// </summary>
        TargetCompleted = 10,

        /// <summary>
        /// 含义：迁移未能提交，请求 Owner Gate 恢复 Source Game 路由。
        /// 方向：Source Game → Owner Gate。
        /// 状态影响：Gate 把缓存消息按原路由回放到 Source Game，丢弃仅由 Target Game 产生且不应回放的 Client RPC，恢复原路由并解除冻结。
        /// </summary>
        RollbackRoute = 11,

        /// <summary>
        /// 含义：Owner Gate 返回 Avatar 路由回滚结果。
        /// 方向：Owner Gate → Source Game。
        /// 状态影响：Source Game 尝试让 Avatar 重新进入迁移前的 Space，随后把迁移置为 Failed 并移除源迁移上下文。
        /// </summary>
        RouteRolledBack = 12,

        /// <summary>
        /// 含义：请求 Target Game 放弃已经准备或激活的目标 Avatar；目标清理完成后才能回滚 Gate 路由。
        /// 方向：Source Game → Owner Gate → Target Game。
        /// 状态影响：Target Game 从目标 Space 移除并注销已激活的 Avatar，把目标 Avatar 置为 Failed，记录中止编号并移除目标迁移上下文。
        /// </summary>
        AbortTarget = 13,

        /// <summary>
        /// 含义：Target Game 返回目标 Avatar 的中止结果。
        /// 方向：Target Game → Owner Gate → Source Game。
        /// 状态影响：成功时 Source Game 确认目标已中止并发送 RollbackRoute；失败时保持 RollingBack，并记录错误等待外部处理。
        /// </summary>
        TargetAborted = 14,
    }

    internal sealed class AvatarMigrationMessage
    {
        public const ushort CurrentVersion = 1;

        public ushort Version { get; set; } = CurrentVersion;
        public AvatarMigrationCommand Command { get; set; }
        public Guid MigrationId { get; set; }
        public Guid AvatarId { get; set; }
        public Guid TargetSpaceId { get; set; }
        public string SourceGameServerId { get; set; } = string.Empty;
        public string TargetGameServerId { get; set; } = string.Empty;
        public string GateServerId { get; set; } = string.Empty;
        public ulong ClientSessionId { get; set; }
        public bool Success { get; set; } = true;
        public string Error { get; set; } = string.Empty;
        public byte[] AvatarData { get; set; } = Array.Empty<byte>();

        public AvatarMigrationMessage CreateResponse(AvatarMigrationCommand command, bool success, string error = null)
        {
            return new AvatarMigrationMessage
            {
                Command = command,
                MigrationId = MigrationId,
                AvatarId = AvatarId,
                TargetSpaceId = TargetSpaceId,
                SourceGameServerId = SourceGameServerId,
                TargetGameServerId = TargetGameServerId,
                GateServerId = GateServerId,
                ClientSessionId = ClientSessionId,
                Success = success,
                Error = error ?? string.Empty,
            };
        }
    }

    internal static class AvatarMigrationProtocol
    {
        private const int MaxPayloadSizeBytes = 16 * 1024 * 1024;
        private static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
        };

        public static byte[] BuildServerRpcPayload(
            ServerRpcTargetKind targetKind,
            string targetServerId,
            AvatarMigrationMessage message)
        {
            var messagePayload = JsonSerializer.SerializeToUtf8Bytes(message, s_jsonOptions);
            return new ServerRpcPayload
            {
                Version = ServerRpcPayload.CurrentVersion,
                TargetKind = targetKind,
                EntityId = message.AvatarId,
                TargetServerId = targetServerId ?? string.Empty,
                StubName = string.Empty,
                MethodId = (uint)message.Command,
                ArgsPayload = messagePayload,
            }.Serialize();
        }

        public static bool TryDeserialize(ServerRpcPayload rpc, out AvatarMigrationMessage message)
        {
            message = null;
            if (rpc.ArgsPayload == null
                || rpc.ArgsPayload.Length == 0
                || rpc.ArgsPayload.Length > MaxPayloadSizeBytes)
            {
                return false;
            }

            try
            {
                message = JsonSerializer.Deserialize<AvatarMigrationMessage>(rpc.ArgsPayload, s_jsonOptions);
            }
            catch (JsonException)
            {
                return false;
            }

            return message != null
                && message.Version == AvatarMigrationMessage.CurrentVersion
                && message.MigrationId != Guid.Empty
                && message.AvatarId != Guid.Empty
                && message.AvatarId == rpc.EntityId
                && (uint)message.Command == rpc.MethodId
                && !string.IsNullOrWhiteSpace(message.SourceGameServerId)
                && !string.IsNullOrWhiteSpace(message.TargetGameServerId)
                && !string.IsNullOrWhiteSpace(message.GateServerId);
        }
    }

    internal sealed class SourceAvatarMigrationContext
    {
        public AvatarEntity Avatar;
        public AvatarMigrationMessage Message;
        public EntityMailBox TargetSpace;
        public Guid SourceSpaceId;
        public byte[] AvatarData = Array.Empty<byte>();
        public bool TargetWasPrepared;
    }

    internal sealed class TargetAvatarMigrationContext
    {
        public AvatarEntity Avatar;
        public AvatarMigrationMessage Message;
        public bool IsActivated;
    }

    internal enum QueuedAvatarMigrationMessageKind
    {
        AvatarRpcToGame,
        ServerRpcToGame,
        AvatarRpcToClient,
    }

    internal sealed class QueuedAvatarMigrationMessage
    {
        public QueuedAvatarMigrationMessageKind Kind;
        public string SourceServerId = string.Empty;
        public byte[] Payload = Array.Empty<byte>();
    }

    internal sealed class GateAvatarMigrationContext
    {
        public AvatarMigrationMessage Message;
        public readonly Queue<QueuedAvatarMigrationMessage> Messages = new Queue<QueuedAvatarMigrationMessage>();
    }
}
