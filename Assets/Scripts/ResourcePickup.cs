using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ResourcePickup : UdonSharpBehaviour
{
    public int itemType;
    public int slotIndex = -1;
    public ResourceSync resourceSync;

    public override void OnPickup()
    {
        if (resourceSync == null || slotIndex < 0) return;
        VRCPlayerApi lp = Networking.LocalPlayer;
        if (lp == null) return;
        int handBit = 0;
        VRCPickup pk = (VRCPickup)GetComponent(typeof(VRCPickup));
        if (pk != null && pk.currentHand == VRC_Pickup.PickupHand.Right) handBit = 1;
        resourceSync.SendCustomNetworkEvent(NetworkEventTarget.Owner, "RequestSetHolder", itemType, slotIndex, (lp.playerId << 1) | handBit);
    }

    public override void OnDrop()
    {
        if (resourceSync == null || slotIndex < 0) return;
        Vector3 p = transform.position;
        resourceSync.SendCustomNetworkEvent(NetworkEventTarget.Owner, "RequestDrop", itemType, slotIndex, p.x, p.z);
    }

    public bool SyncedHeld()
    {
        if (resourceSync == null || slotIndex < 0) return false;
        return resourceSync.IsHeldSynced(itemType, slotIndex);
    }
}
