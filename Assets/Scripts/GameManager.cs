using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class GameManager : UdonSharpBehaviour
{
    public const int StateIdle = 0;
    public const int StateCountdown = 1;
    public const int StateRunning = 2;
    public const int StateCrashed = 3;
    public const int StateStationStop = 4;

    [UdonSynced] public int runState;
    [UdonSynced] public int runSeed;
    [UdonSynced] public int score;
    [UdonSynced] public int stationCount;
    [UdonSynced] public int woodCount;
    [UdonSynced] public int ironCount;
    [UdonSynced] public int railStock;

    public TrackManager trackManager;
    public TrainController trainController;
    public ChunkManager chunkManager;
    public ScorePersistence scorePersistence;
    public RailStackWagon railStackWagon;

    public float countdownSeconds = 3f;
    public float crashResetDelay = 5f;
    public float stationStopSeconds = 10f;
    public int stationTiles = 30;
    public int railStockMax = 8;
    public int woodMax = 8;
    public int ironMax = 8;

    private bool _wasCrashed;

    void Start()
    {
        ClaimOwnershipIfMaster();
        if (Networking.IsOwner(gameObject) && trackManager.trackLength == 0)
        {
            OwnerResetRun();
        }
        else
        {
            ApplyState();
        }
    }

    private void ClaimOwnershipIfMaster()
    {
        if (Networking.IsMaster && !Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }
    }

    public override void OnMasterTransferred(VRCPlayerApi newMaster)
    {
        ClaimOwnershipIfMaster();
    }

    [NetworkCallable]
    public void RequestStartRun()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (runState != StateIdle) return;
        runState = StateCountdown;
        RequestSerialization();
        ApplyState();
        SendCustomEventDelayedSeconds(nameof(_BeginRun), countdownSeconds);
    }

    public void _BeginRun()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (runState != StateCountdown) return;
        runState = StateRunning;
        RequestSerialization();
        ApplyState();
    }

    public void NotifyStationReached()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (runState != StateRunning) return;
        runState = StateStationStop;
        stationCount++;
        RequestSerialization();
        ApplyState();
        SendCustomEventDelayedSeconds(nameof(_DepartTrain), stationStopSeconds);
    }

    [NetworkCallable]
    public void RequestDepart()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (runState != StateStationStop) return;
        DepartNow();
    }

    public void _DepartTrain()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (runState != StateStationStop) return;
        DepartNow();
    }

    private void DepartNow()
    {
        runState = StateRunning;
        RequestSerialization();
        ApplyState();
    }

    public void NotifyDerailed()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (runState != StateRunning) return;
        runState = StateCrashed;
        score = ComputeScore();
        RequestSerialization();
        ApplyState();
        SendCustomEventDelayedSeconds(nameof(_ResetAfterCrash), crashResetDelay);
    }

    private int ComputeScore()
    {
        return stationCount * 100 + Mathf.FloorToInt(trainController.trackDistance);
    }

    public void _ResetAfterCrash()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (runState != StateCrashed) return;
        OwnerResetRun();
    }

    public void OwnerResetRun()
    {
        if (!Networking.IsOwner(gameObject)) return;
        runSeed = UnityEngine.Random.Range(1, 1000000);
        stationCount = 0;
        score = 0;
        woodCount = 0;
        ironCount = 0;
        runState = StateIdle;
        railStock = 0;
        trainController.OwnerResetDistance();
        trackManager.OwnerBuildStarterTrack();
        trackManager.OwnerReturnAllRails();
        if (chunkManager != null)
        {
            chunkManager.OwnerResetChunks();
            chunkManager.OwnerReturnAllPickups();
        }
        RequestSerialization();
        ApplyState();
    }

    public void OwnerDepositResource(int itemType, GameObject pickupObject)
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (itemType == 0)
        {
            if (woodCount >= woodMax) return;
            woodCount++;
        }
        else if (itemType == 1)
        {
            if (ironCount >= ironMax) return;
            ironCount++;
        }
        else return;
        if (chunkManager != null) chunkManager.OwnerReturnPickup(itemType, pickupObject);
        RequestSerialization();
    }

    public bool OwnerConsumeWood(int n)
    {
        if (!Networking.IsOwner(gameObject)) return false;
        if (woodCount < n) return false;
        woodCount -= n;
        RequestSerialization();
        return true;
    }

    public bool OwnerConsumeCraftResources()
    {
        if (!Networking.IsOwner(gameObject)) return false;
        if (woodCount < 1 || ironCount < 1) return false;
        woodCount--;
        ironCount--;
        RequestSerialization();
        return true;
    }

    public void OwnerAddRailStock(int n)
    {
        if (!Networking.IsOwner(gameObject)) return;
        railStock = Mathf.Min(railStock + n, railStockMax);
        RequestSerialization();
    }

    [NetworkCallable]
    public void RequestWithdrawRail()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (railStock <= 0) return;
        railStock--;
        Vector3 pos = railStackWagon != null && railStackWagon.outputMarker != null
            ? railStackWagon.outputMarker.position
            : transform.position + Vector3.up;
        trackManager.OwnerSpawnRailItem(pos);
        RequestSerialization();
    }

    public override void OnDeserialization()
    {
        ApplyState();
    }

    private void ApplyState()
    {
        bool crashed = runState == StateCrashed;
        if (crashed && !_wasCrashed)
        {
            trainController.PlayCrashFX();
            if (scorePersistence != null) scorePersistence.ReportScore(score);
        }
        _wasCrashed = crashed;
    }
}
