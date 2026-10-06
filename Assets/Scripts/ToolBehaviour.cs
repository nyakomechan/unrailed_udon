using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
public class ToolBehaviour : UdonSharpBehaviour
{
    public int toolType;
    public ChunkManager chunkManager;
    public float tickInterval = 0.5f;

    private float _lastTickTime = -999f;
    private int _lastTarget = -1;

    void Start()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }


    void OnTriggerEnter(Collider collision)
    {
        TryHarvest(collision.transform.position);
    }
    void OnTriggerStay(Collider collision)
    {
        TryHarvest(collision.transform.position);
    }

    public override void OnPickupUseDown()
    {
        if (!Networking.IsOwner(gameObject)) return;

        Vector3 point = Vector3.zero;
        bool found = false;
        float best = float.MaxValue;

        Vector3 headPos = transform.TransformPoint(0f, 0.45f, 0f);
        Collider[] near = Physics.OverlapSphere(headPos, 0.85f, 1, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < near.Length; i++)
        {
            if (near[i].GetComponentInParent<VRCPickup>() != null) continue;
            float d = (near[i].transform.position - headPos).sqrMagnitude;
            if (d < best) { best = d; point = near[i].transform.position; found = true; }
        }

        if (!found)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (lp == null) return;
            VRCPlayerApi.TrackingData head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Ray ray = new Ray(head.position, head.rotation * Vector3.forward);
            RaycastHit[] hits = Physics.RaycastAll(ray, 4f, 1, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider.GetComponentInParent<VRCPickup>() != null) continue;
                if (hits[i].distance < best) { best = hits[i].distance; point = hits[i].point; found = true; }
            }
        }

        if (found) TryHarvest(point);
    }

    private void TryHarvest(Vector3 p)
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (chunkManager == null) return;

        int tx = Mathf.FloorToInt(p.x - chunkManager.origin.x + 0.5f);
        int tz = Mathf.FloorToInt(p.z - chunkManager.origin.z + 0.5f);

        int target = (tx << 8) | (tz & 255);
        if (target != _lastTarget || Time.time - _lastTickTime >= tickInterval)
        {
            _lastTarget = target;
            _lastTickTime = Time.time;
            chunkManager.SendCustomNetworkEvent(NetworkEventTarget.Owner, "RequestHarvestTile", tx, tz, toolType);
        }
    }
}
