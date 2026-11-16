using System;
using System.Collections.Generic;

namespace DE.Client.Entities
{
    public enum EntitySerializationProfile : byte
    {
        Owner = 1,
        Observer = 2,
        Migration = 3,
    }

    public enum EntitySerializationMode : byte
    {
        Snapshot = 1,
        Patch = 2,
    }

    [Flags]
    public enum EntitySerializationProfileMask : byte
    {
        None = 0,
        Owner = 1,
        Observer = 2,
        Migration = 4,
    }

    /// <summary>一次属性树读取的上下文；验证和应用使用独立实例，识别标记按 Patch 记录重置。</summary>
    public sealed class EntityPropertyReadContext
    {
        public EntityPropertyReadContext(
            EntitySerializationProfile profile, EntitySerializationMode mode, bool apply, Guid? expectedEntityId = null)
        {
            Profile = profile;
            Mode = mode;
            Apply = apply;
            ExpectedEntityId = expectedEntityId;
        }

        public EntitySerializationProfile Profile { get; }
        public EntitySerializationMode Mode { get; }
        public bool Apply { get; }
        public Guid? ExpectedEntityId { get; }
        public bool PropertyRecognized { get; private set; }
        public int Depth { get; internal set; }

        public void ResetPropertyRecognition() => PropertyRecognized = false;
        public void MarkRecognized() => PropertyRecognized = true;
        public void Enter() => Depth = checked(Depth + 1);

        public void Exit()
        {
            if (Depth == 0)
            {
                throw new InvalidOperationException("The property reader is already at the root.");
            }
            Depth--;
        }
    }

    /// <summary>属性包完整应用后的状态通知；Changes 携带本次涉及属性的最新值，不表示赋值历史或跨属性顺序。</summary>
    public sealed class EntityPropertyApplyContext
    {
        public EntityPropertyApplyContext(
            EntitySerializationProfile profile,
            EntitySerializationMode mode,
            ulong baseSequence,
            ulong throughSequence,
            IReadOnlyList<EntityPropertyChange> changes,
            bool initializing = false)
        {
            if (changes == null)
            {
                throw new ArgumentNullException(nameof(changes));
            }
            var copy = new EntityPropertyChange[changes.Count];
            for (int index = 0; index < copy.Length; index++)
            {
                copy[index] = changes[index] ?? throw new ArgumentException("Changes cannot contain null.", nameof(changes));
            }
            Profile = profile;
            Mode = mode;
            BaseSequence = baseSequence;
            ThroughSequence = throughSequence;
            Changes = Array.AsReadOnly(copy);
            Initializing = initializing;
        }

        public EntitySerializationProfile Profile { get; }
        public EntitySerializationMode Mode { get; }
        public ulong BaseSequence { get; }
        public ulong ThroughSequence { get; }
        public IReadOnlyList<EntityPropertyChange> Changes { get; }
        public bool Initializing { get; }
    }
}
