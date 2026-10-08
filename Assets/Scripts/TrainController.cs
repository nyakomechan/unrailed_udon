using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class TrainController : UdonSharpBehaviour
{
    [UdonSynced] public float trackDistance;
    [UdonSynced] public bool stationBoost;

    public GameManager gameManager;
    public TrackManager trackManager;
    public Transform trainVisual;
    public Transform[] wagonVisuals = new Transform[0];
    public float wagonSpacing = 2f;
    public ParticleSystem crashParticles;

    public float baseSpeed = 0.25f;
    public float speedPerStation = 0.12f;
    public float stationBoostMultiplier = 3f;
    public float derailMargin = 0.5f;
    public float syncInterval = 10f;
    public float remoteCorrectRate = 3f;
    public ParticleSystem fireParticles;
    public AudioSource waterAlarmSfx;
    public AudioSource crashSfx;

    private float _displayDistance;
    private float _remoteBase;
    private float _remoteBaseTime;
    private float _nextSync;
    private int _lastRunState = -1;

    void Start()
    {
        ClaimOwnershipIfMaster();
        _displayDistance = trackDistance;
        _remoteBase = trackDistance;
        _remoteBaseTime = Time.time;
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

    public float CurrentSpeed()
    {
        return (baseSpeed + gameManager.stationCount * speedPerStation) * (stationBoost ? stationBoostMultiplier : 1f);
    }

    public override void OnDeserialization()
    {
        _remoteBase = trackDistance;
        _remoteBaseTime = Time.time;
        if (Mathf.Abs(_displayDistance - trackDistance) > 3f) _displayDistance = trackDistance;
    }

    void Update()
    {
        if (trackManager == null || gameManager == null) return;
        if (trackManager.trackLength <= 0) return;

        int runState = gameManager.runState;

        if (Networking.IsOwner(gameObject))
        {
            if (runState == GameManager.StateRunning)
            {
                bool boost = false;
                if (gameManager.stationTiles > 0)
                {
                    int nextStationX = (gameManager.stationCount + 1) * gameManager.stationTiles;
                    boost = trackManager.IsOnPath(nextStationX - 1, 0);
                }
                if (boost != stationBoost)
                {
                    stationBoost = boost;
                    RequestSerialization();
                }
                trackDistance += CurrentSpeed() * Time.deltaTime;
                float limit = trackManager.GetEndDistance() - derailMargin;
                if (trackDistance >= limit)
                {
                    trackDistance = limit;
                    gameManager.NotifyDerailed();
                }
                else if (gameManager.stationTiles > 0)
                {
                    Vector3 trainPos = trackManager.GetPositionAt(trackDistance);
                    int trainTileX = Mathf.FloorToInt(trainPos.x - trackManager.origin.x + 0.5f);
                    if (trainTileX >= (gameManager.stationCount + 1) * gameManager.stationTiles)
                    {
                        gameManager.NotifyStationReached();
                    }
                }
            }
            else if (stationBoost)
            {
                stationBoost = false;
                RequestSerialization();
            }
            _displayDistance = trackDistance;
            if (runState != _lastRunState)
            {
                RequestSerialization();
                _nextSync = Time.time + syncInterval;
            }
            else if (Time.time >= _nextSync)
            {
                _nextSync = Time.time + syncInterval;
                RequestSerialization();
            }
        }
        else
        {
            float target = _remoteBase;
            if (runState == GameManager.StateRunning)
            {
                target = _remoteBase + CurrentSpeed() * (Time.time - _remoteBaseTime);
                float limit = trackManager.GetEndDistance() - derailMargin;
                if (target > limit) target = limit;
            }
            _displayDistance = Mathf.Lerp(_displayDistance, target, Time.deltaTime * remoteCorrectRate);
        }
        _lastRunState = runState;
        ApplyTransform(_displayDistance);

        bool fire = gameManager.boilerOnFire;
        if (fireParticles != null)
        {
            if (fire && !fireParticles.isPlaying) fireParticles.Play();
            else if (!fire && fireParticles.isPlaying) fireParticles.Stop();
        }

        bool running = runState == GameManager.StateRunning || runState == GameManager.StateStationStop;
        bool alarm = running && !fire && gameManager.waterLevel <= 1;
        if (waterAlarmSfx != null)
        {
            if (alarm && !waterAlarmSfx.isPlaying) waterAlarmSfx.Play();
            else if (!alarm && waterAlarmSfx.isPlaying) waterAlarmSfx.Stop();
        }
    }

    private void ApplyTransform(float d)
    {
        if (trainVisual != null)
        {
            trainVisual.SetPositionAndRotation(trackManager.GetPositionAt(d), Quaternion.LookRotation(SafeTangent(d)));
        }
        for (int i = 0; i < wagonVisuals.Length; i++)
        {
            if (wagonVisuals[i] == null) continue;
            float wd = d - wagonSpacing * (i + 1);
            if (wd >= 0f)
            {
                wagonVisuals[i].SetPositionAndRotation(trackManager.GetPositionAt(wd), Quaternion.LookRotation(SafeTangent(wd)));
            }
            else
            {
                Vector3 t0 = SafeTangent(0f);
                wagonVisuals[i].SetPositionAndRotation(trackManager.GetPositionAt(0f) + t0 * wd, Quaternion.LookRotation(t0));
            }
        }
    }

    private Vector3 SafeTangent(float d)
    {
        Vector3 tan = trackManager.GetTangentAt(d);
        if (tan.sqrMagnitude < 0.001f) tan = Vector3.forward;
        return tan;
    }

    public void OwnerResetDistance()
    {
        if (!Networking.IsOwner(gameObject)) return;
        trackDistance = 0f;
        _displayDistance = 0f;
        stationBoost = false;
        _nextSync = Time.time + syncInterval;
        RequestSerialization();
    }

    public void PlayCrashFX()
    {
        if (crashParticles != null) crashParticles.Play();
        if (crashSfx != null && crashSfx.clip != null) crashSfx.PlayOneShot(crashSfx.clip, 1f);
    }
}
