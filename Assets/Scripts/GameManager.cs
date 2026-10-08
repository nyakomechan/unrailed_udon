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
    [UdonSynced] public int resetSerial;
    [UdonSynced] public int waterLevel;
    [UdonSynced] public bool boilerOnFire;

    public TrackManager trackManager;
    public TrainController trainController;
    public ChunkManager chunkManager;
    public ScorePersistence scorePersistence;
    public RailStackWagon railStackWagon;
    public Transform spawnPoint;
    public BucketManager bucketManager;

    private int _seenSerial = -1;
    private float _waterTimer;
    private float _fireTimer;

    void Update()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (runState != StateRunning && runState != StateStationStop) return;

        if (!boilerOnFire)
        {
            _waterTimer += Time.deltaTime;
            if (_waterTimer >= waterBurnSeconds)
            {
                _waterTimer = 0f;
                if (waterLevel > 0)
                {
                    waterLevel--;
                    if (waterLevel <= 0)
                    {
                        boilerOnFire = true;
                        _fireTimer = 0f;
                    }
                    RequestSerialization();
                }
            }
        }
        else
        {
            _fireTimer += Time.deltaTime;
            if (_fireTimer >= fireGraceSeconds)
            {
                _fireTimer = 0f;
                NotifyDerailed();
            }
        }
    }

    public float countdownSeconds = 3f;
    public float stationStopSeconds = 10f;
    public int stationTiles = 40;
    public int waterMax = 6;
    public float waterBurnSeconds = 25f;
    public float fireGraceSeconds = 20f;
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
        for (int k = stationCount + 1; k <= stationCount + 3; k++) trackManager.OwnerBuildStationStub(k);
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
        if (runState != StateRunning && runState != StateStationStop) return;
        runState = StateCrashed;
        score = ComputeScore();
        RequestSerialization();
        ApplyState();
    }

    private int ComputeScore()
    {
        return stationCount * 100 + Mathf.FloorToInt(trainController.trackDistance);
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
        waterLevel = waterMax;
        boilerOnFire = false;
        _waterTimer = 0f;
        _fireTimer = 0f;
        trainController.OwnerResetDistance();
        if (bucketManager != null) bucketManager.OwnerResetBuckets();
        trackManager.OwnerBuildStarterTrack();
        for (int k = 1; k <= 3; k++) trackManager.OwnerBuildStationStub(k);
        trackManager.OwnerReturnAllRails();
        if (chunkManager != null)
        {
            chunkManager.OwnerResetChunks();
            chunkManager.OwnerReturnAllPickups();
        }
        resetSerial++;
        RequestSerialization();
        ApplyState();
        RespawnLocalPlayer();
    }

    private void RespawnLocalPlayer()
    {
        VRCPlayerApi lp = Networking.LocalPlayer;
        if (lp == null || spawnPoint == null) return;
        lp.TeleportTo(spawnPoint.position, spawnPoint.rotation);
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

    public void OwnerAddWater(int n)
    {
        if (!Networking.IsOwner(gameObject)) return;
        waterLevel = Mathf.Min(waterLevel + n, waterMax);
        if (boilerOnFire && waterLevel > 0)
        {
            boilerOnFire = false;
            _fireTimer = 0f;
        }
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
        if (_seenSerial < 0)
        {
            _seenSerial = resetSerial;
        }
        else if (resetSerial != _seenSerial)
        {
            _seenSerial = resetSerial;
            RespawnLocalPlayer();
        }
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
