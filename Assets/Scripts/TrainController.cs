using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
public class TrainController : UdonSharpBehaviour
{
    [UdonSynced] public float trackDistance;

    public GameManager gameManager;
    public TrackManager trackManager;
    public Transform trainVisual;
    public Transform[] wagonVisuals = new Transform[0];
    public float wagonSpacing = 2f;
    public ParticleSystem crashParticles;

    public float baseSpeed = 0.25f;
    public float speedPerStation = 0.12f;
    public float derailMargin = 0.5f;
    public float remoteLerpRate = 10f;

    private float _displayDistance;

    void Start()
    {
        ClaimOwnershipIfMaster();
        _displayDistance = trackDistance;
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
        return baseSpeed + gameManager.stationCount * speedPerStation;
    }

    void Update()
    {
        if (trackManager == null || gameManager == null) return;
        if (trackManager.trackLength <= 0) return;

        if (Networking.IsOwner(gameObject))
        {
            if (gameManager.runState == GameManager.StateRunning)
            {
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
            _displayDistance = trackDistance;
        }
        else
        {
            _displayDistance = Mathf.Lerp(_displayDistance, trackDistance, Time.deltaTime * remoteLerpRate);
        }
        ApplyTransform(_displayDistance);
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
    }

    public void PlayCrashFX()
    {
        if (crashParticles != null) crashParticles.Play();
    }
}
