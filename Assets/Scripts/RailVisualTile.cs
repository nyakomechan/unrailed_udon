using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class RailVisualTile : UdonSharpBehaviour
{
    public TrackManager trackManager;
    public GameObject bridgeVisual;
    public GameObject railL;
    public GameObject railR;
    public GameObject curveA;
    public GameObject curveB;

    [System.NonSerialized] public int tileIndex;

    public override void Interact()
    {
        if (trackManager == null) return;
        if (!gameObject.activeSelf) return;
        trackManager.SendCustomNetworkEvent(NetworkEventTarget.Owner, "RequestRemoveRail", tileIndex);
    }
}

