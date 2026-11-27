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

        /// <summary>Avatar 是 Ghost 或尚未收到 MigrationOver；只有 Real 可以再次传送。</summary>
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

        /// <summary>目标场景的 CanEnter 拒绝入场；已恢复原场景，原本没有场景的 Avatar 则保持无场景状态。</summary>
        EnterTargetSpaceFailed,

        /// <summary>目标拒绝入场，原场景也未通过 CanEnter；Avatar 当前没有场景，需要调用方处理。</summary>
        RestoreSourceSpaceFailed,

        /// <summary>Avatar 的 IsAllowImmigrate 返回 false，禁止跨 Game 迁移。</summary>
        ImmigrationNotAllowed,

        /// <summary>Avatar 的 Gate Proxy 无效，无法通知 OwnerGate 并中继跨 Game 迁移。</summary>
        InvalidAvatarProxy,

        /// <summary>MigrationStart 发送失败，源 Avatar 尚未改变角色或离场。</summary>
        MigrationStartRequestFailed,

        /// <summary>迁移快照准备失败；已尝试恢复源 Avatar。</summary>
        MigrationSnapshotFailed,

        /// <summary>TeleportToGame 发送失败；已尝试恢复源 Avatar。</summary>
        TransferRequestFailed,
    }
}
