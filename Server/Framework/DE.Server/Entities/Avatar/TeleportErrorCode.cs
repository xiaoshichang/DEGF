namespace DE.Server.Entities
{
    public enum TeleportErrorCode
    {
        /// <summary>本地入场已完成，或跨 Game 迁移已成功发起；后者仍需等待异步迁移结果。</summary>
        Success = 0,

        /// <summary>传入的 Avatar 为 null，无法执行传送。</summary>
        AvatarIsNull,

        /// <summary>Avatar 的 Guid 为空，尚未建立有效的实体身份。</summary>
        AvatarIdIsEmpty,

        /// <summary>目标 MailBox 的实体 ID 或绑定 Game 无效，无法确定目标场景。</summary>
        InvalidTargetSpaceMailBox,

        /// <summary>当前 Game 的 Avatar 注册表中不存在该 Guid。</summary>
        AvatarNotRegistered,

        /// <summary>该 Guid 已注册，但对应的 Avatar 实例与传入实例不同。</summary>
        AvatarInstanceMismatch,

        /// <summary>Avatar 正处于迁移中，不能同时执行本地传送；本地传送仅允许 Idle、Completed、Failed 状态。</summary>
        ImmigrationInProgress,

        /// <summary>目标绑定当前 Game，但其实体 ID 不存在于本地实体注册表中。</summary>
        TargetSpaceNotFound,

        /// <summary>目标实体存在，但不是 ServerSpaceEntity，不能作为入场目标。</summary>
        TargetEntityIsNotSpace,

        /// <summary>Avatar 已属于目标场景，重复传送到同一场景被拒绝。</summary>
        AlreadyInTargetSpace,

        /// <summary>目标场景设置了人数上限，且当前人数已达到上限；Avatar 保持原场景不变。</summary>
        TargetSpaceFull,

        /// <summary>原场景的 Leave 返回失败，未继续尝试进入目标场景。</summary>
        LeaveSourceSpaceFailed,

        /// <summary>目标场景的 Enter 返回失败；已恢复原场景，原本没有场景的 Avatar 则继续保持无场景状态。</summary>
        EnterTargetSpaceFailed,

        /// <summary>目标场景入场失败后，重新进入原场景也失败；Avatar 当前未属于任何场景，需要调用方处理。</summary>
        RestoreSourceSpaceFailed,

        /// <summary>跨 Game 迁移仅允许从 Idle 开始；迁移中及 Completed、Failed 终态都不能再次发起迁移。</summary>
        ImmigrationStateNotIdle,

        /// <summary>Avatar 的 IsAllowImmigrate 返回 false，禁止跨 Game 迁移。</summary>
        ImmigrationNotAllowed,

        /// <summary>Avatar 的 Gate Proxy 无效，无法向 OwnerGate 发起跨 Game 路由冻结。</summary>
        InvalidAvatarProxy,

        /// <summary>首个 FreezeRoute 请求发送失败；已移除源迁移上下文并将 Avatar 状态置为 Failed。</summary>
        FreezeRouteRequestFailed,
    }
}
