using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class WagonDepositZone : UdonSharpBehaviour
{
    public GameManager gameManager;

    void OnTriggerStay(Collider other)
    {
        if (!Networking.IsMaster) return;
        if (gameManager == null) return;
        if (!other.gameObject.activeInHierarchy) return;

        ResourcePickup pickup = other.GetComponent<ResourcePickup>();
        if (pickup == null) return;

        VRCPickup vrcPickup = other.GetComponent<VRCPickup>();
        if (vrcPickup != null && vrcPickup.IsHeld) return;
        if (pickup.SyncedHeld()) return;

        gameManager.OwnerDepositResource(pickup.itemType, other.gameObject);
    }
}
