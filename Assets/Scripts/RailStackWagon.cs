using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class RailStackWagon : UdonSharpBehaviour
{
    public GameManager gameManager;
    public Transform outputMarker;
    public GameObject[] stackVisuals = new GameObject[8];

    private int _appliedCount = -1;

    void Update()
    {
        if (gameManager == null) return;
        int c = gameManager.railStock;
        if (c == _appliedCount) return;
        _appliedCount = c;
        for (int i = 0; i < stackVisuals.Length; i++)
        {
            GameObject g = stackVisuals[i];
            if (g == null) continue;
            bool show = i < c;
            if (g.activeSelf != show) g.SetActive(show);
        }
    }

    public override void Interact()
    {
        if (gameManager == null) return;
        if (gameManager.railStock <= 0) return;
        gameManager.SendCustomNetworkEvent(NetworkEventTarget.Owner, "RequestWithdrawRail");
    }
}

