using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class LeverBehaviour : UdonSharpBehaviour
{
    public GameManager gameManager;

    public override void Interact()
    {
        if (gameManager == null) return;
        if (gameManager.runState == GameManager.StateIdle)
        {
            gameManager.SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(GameManager.RequestStartRun));
        }
    }
}

