using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class BucketManager : UdonSharpBehaviour
{
    public ChunkManager chunkManager;
    public GameManager gameManager;
    public Transform tankMarker;
    public Transform[] bucketRackSlots = new Transform[0];
    public BucketBehaviour[] buckets = new BucketBehaviour[0];
    public GameObject[] bucketWaterVisuals = new GameObject[0];
    public float snapDistance = 15f;
    public float settleDistance = 1.5f;
    public float pourVerifyDistance = 3.5f;
    public AudioClip scoopClip;
    public AudioClip pourClip;
    public AudioSource[] bucketSfxSources = new AudioSource[0];

    [UdonSynced] public int bucketMask;

    private int _appliedBucketMask = -1;

    private float _nextSnapCheck;
    private VRCPlayerApi[] _players = new VRCPlayerApi[8];
    private VRC.SDK3.Components.VRCPickup[] _pickups;

    void Start()
    {
        ClaimOwnershipIfMaster();
        ApplyVisuals();
    }

    private void ClaimOwnershipIfMaster()
    {
        if (!Networking.IsMaster) return;
        if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
    }

    public override void OnMasterTransferred(VRCPlayerApi newMaster)
    {
        ClaimOwnershipIfMaster();
    }

    public bool IsFull(int idx)
    {
        return (bucketMask & (1 << idx)) != 0;
    }

    [NetworkCallable]
    public void RequestScoop(int idx)
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (idx < 0 || idx >= buckets.Length) return;
        if (IsFull(idx)) return;
        if (chunkManager == null) return;

        Vector3 bp = buckets[idx].transform.position;
        int bx = Mathf.FloorToInt(bp.x - chunkManager.origin.x + 0.5f);
        int bz = Mathf.FloorToInt(bp.z - chunkManager.origin.z + 0.5f);
        bool nearPond = false;
        for (int dx = -1; dx <= 1 && !nearPond; dx++)
        {
            for (int dz = -1; dz <= 1 && !nearPond; dz++)
            {
                if (chunkManager.GetTypeAtTile(bx + dx, bz + dz) == 3) nearPond = true;
            }
        }
        if (!nearPond) return;

        bucketMask |= 1 << idx;
        RequestSerialization();
        ApplyVisuals();
    }

    [NetworkCallable]
    public void RequestPour(int idx)
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (idx < 0 || idx >= buckets.Length) return;
        if (!IsFull(idx)) return;
        if (tankMarker != null && Vector3.Distance(buckets[idx].transform.position, tankMarker.position) > pourVerifyDistance) return;

        bucketMask &= ~(1 << idx);
        if (gameManager != null) gameManager.OwnerAddWater(1);
        RequestSerialization();
        ApplyVisuals();
    }

    public void OwnerResetBuckets()
    {
        if (!Networking.IsOwner(gameObject)) return;
        bucketMask = 0;
        RequestSerialization();
        ApplyVisuals();
        SendCustomEventDelayedFrames(nameof(_ResetBucketTransforms), 2);
    }

    public void _ResetBucketTransforms()
    {
        if (!Networking.IsOwner(gameObject)) return;
        for (int i = 0; i < buckets.Length; i++)
        {
            if (buckets[i] == null) continue;
            if (i >= bucketRackSlots.Length || bucketRackSlots[i] == null) continue;
            Rigidbody rb = buckets[i].GetComponent<Rigidbody>();
            if (rb == null) continue;
            SnapToRack(i, rb);
        }
    }

    private void SnapToRack(int i, Rigidbody rb)
    {
        if (!Networking.IsOwner(buckets[i].gameObject)) Networking.SetOwner(Networking.LocalPlayer, buckets[i].gameObject);
        rb.isKinematic = true;
        buckets[i].transform.SetPositionAndRotation(bucketRackSlots[i].position, bucketRackSlots[i].rotation);
    }

    public override void OnDeserialization()
    {
        ApplyVisuals();
    }

    private void ApplyVisuals()
    {
        for (int i = 0; i < bucketWaterVisuals.Length; i++)
        {
            if (bucketWaterVisuals[i] == null) continue;
            bool want = IsFull(i);
            if (bucketWaterVisuals[i].activeSelf != want) bucketWaterVisuals[i].SetActive(want);
        }
        if (_appliedBucketMask >= 0)
        {
            for (int i = 0; i < buckets.Length; i++)
            {
                bool was = (_appliedBucketMask & (1 << i)) != 0;
                bool now = IsFull(i);
                if (was == now) continue;
                AudioClip clip = now ? scoopClip : pourClip;
                if (clip != null && i < bucketSfxSources.Length && bucketSfxSources[i] != null)
                    bucketSfxSources[i].PlayOneShot(clip, 0.9f);
            }
        }
        _appliedBucketMask = bucketMask;
    }

    void LateUpdate()
    {
        if (!Networking.IsOwner(gameObject)) return;
        bool snapTick = Time.time >= _nextSnapCheck;
        if (snapTick) _nextSnapCheck = Time.time + 0.5f;

        for (int i = 0; i < buckets.Length; i++)
        {
            if (buckets[i] == null || i >= bucketRackSlots.Length || bucketRackSlots[i] == null) continue;
            Transform b = buckets[i].transform;
            Rigidbody rb = buckets[i].GetComponent<Rigidbody>();
            if (rb == null) continue;
            Vector3 slotPos = bucketRackSlots[i].position;
            float dist = Vector3.Distance(b.position, slotPos);

            if (dist > snapDistance)
            {
                if (!snapTick) continue;
                if (IsHeld(i) || IsAnyPlayerNear(b.position, 3f)) continue;
                SnapToRack(i, rb);
            }
            else if (rb.isKinematic)
            {
                if (dist > 0.02f && !IsAnyPlayerNear(b.position, 3f))
                    b.SetPositionAndRotation(slotPos, bucketRackSlots[i].rotation);
            }
            else
            {
                if (dist <= settleDistance && !IsAnyPlayerNear(b.position, 3f)) SnapToRack(i, rb);
            }
        }
    }

    private bool IsHeld(int i)
    {
        if (_pickups == null || _pickups.Length != buckets.Length)
        {
            _pickups = new VRC.SDK3.Components.VRCPickup[buckets.Length];
            for (int k = 0; k < buckets.Length; k++)
                if (buckets[k] != null) _pickups[k] = (VRC.SDK3.Components.VRCPickup)buckets[k].GetComponent(typeof(VRC.SDK3.Components.VRCPickup));
        }
        var p = _pickups[i];
        return p != null && p.IsHeld;
    }

    private bool IsAnyPlayerNear(Vector3 pos, float r)
    {
        int n = VRCPlayerApi.GetPlayerCount();
        if (_players.Length < n) _players = new VRCPlayerApi[n + 8];
        VRCPlayerApi.GetPlayers(_players);
        for (int i = 0; i < n; i++)
        {
            if (_players[i] == null || !_players[i].IsValid()) continue;
            if (Vector3.Distance(_players[i].GetPosition(), pos) <= r) return true;
        }
        return false;
    }
}
