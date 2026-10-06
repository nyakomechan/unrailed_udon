using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class CraftingWagon : UdonSharpBehaviour
{
    public GameManager gameManager;
    public TrackManager trackManager;
    public Transform outputMarker;
    public float craftSeconds = 3f;
    public int railsPerCraft = 2;

    private bool _craftBusy;

    void Update()
    {
        if (!Networking.IsMaster) return;
        if (_craftBusy) return;
        if (gameManager == null || trackManager == null) return;
        if (gameManager.woodCount >= 1 && gameManager.ironCount >= 1
            && gameManager.railStock < gameManager.railStockMax)
        {
            if (gameManager.OwnerConsumeCraftResources())
            {
                _craftBusy = true;
                SendCustomEventDelayedSeconds(nameof(_FinishCraft), craftSeconds);
            }
        }
    }

    public void _FinishCraft()
    {
        _craftBusy = false;
        if (!Networking.IsMaster) return;
        gameManager.OwnerAddRailStock(railsPerCraft);
    }
}
