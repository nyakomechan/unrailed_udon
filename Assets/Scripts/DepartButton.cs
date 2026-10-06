using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class DepartButton : UdonSharpBehaviour
{
    public GameManager gameManager;

    public override void Interact()
    {
        if (gameManager == null) return;
        if (gameManager.runState != GameManager.StateStationStop) return;
        gameManager.SendCustomNetworkEvent(NetworkEventTarget.Owner, "RequestDepart");
    }
}

