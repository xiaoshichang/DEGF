using System;
using DE.Share.Rpc;

namespace DE.Client.Entities
{
    public partial class ClientAvatarEntity
    {
        [ClientRpc]
        public void OnEntityPropertyPatch(Guid entityId, int profileValue, byte[] data)
        {
            var profile = (EntitySerializationProfile)profileValue;
            var target = entityId == Guid ? this : TransformCmpt.Spaces.FindPropertyTarget(entityId);
            if (target == null)
            {
                // Departed entities have no live property tree. Avatar creation still precedes all Avatar RPCs.
                return;
            }
            if (profile != (ReferenceEquals(target, this) ? EntitySerializationProfile.Owner : EntitySerializationProfile.Observer)
                || !EntitySerializer.TryApplyPatch(target, profile, data))
            {
                UnityEngine.Debug.LogError("Rejected entity property patch, entityId=" + entityId + ", profile=" + profileValue);
            }
        }
    }
}
