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
    public AudioSource swingSource;

    private float _lastTickTime = -999f;

    void OnTriggerEnter(Collider col)
    {
        TryHarvest(col);
    }

    void OnTriggerStay(Collider col)
    {
        TryHarvest(col);
    }

    public override void OnPickupUseDown()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (Time.time - _lastTickTime < tickInterval) return;

        Vector3 point = Vector3.zero;
        bool found = false;
        float best = float.MaxValue;

        Vector3 headPos = transform.TransformPoint(0f, 0.45f, 0f);
        Collider[] near = Physics.OverlapSphere(headPos, 0.85f, 1, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < near.Length; i++)
        {
            if (!IsNodeCollider(near[i])) continue;
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
                if (!IsNodeCollider(hits[i].collider)) continue;
                if (hits[i].distance < best) { best = hits[i].distance; point = hits[i].point; found = true; }
            }
        }

        if (found) SendHarvest(point);
    }

    private bool IsNodeCollider(Collider col)
    {
        return col != null && col.name.StartsWith("Node");
    }

    private void TryHarvest(Collider col)
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (!IsNodeCollider(col)) return;
        if (Time.time - _lastTickTime < tickInterval) return;
        SendHarvest(col.transform.position);
    }

    private void SendHarvest(Vector3 p)
    {
        if (chunkManager == null) return;
        _lastTickTime = Time.time;
        int tx = Mathf.FloorToInt(p.x - chunkManager.origin.x + 0.5f);
        int tz = Mathf.FloorToInt(p.z - chunkManager.origin.z + 0.5f);
        chunkManager.SendCustomNetworkEvent(NetworkEventTarget.Owner, "RequestHarvestTile", tx, tz, toolType);
        if (swingSource != null) swingSource.PlayOneShot(swingSource.clip);
    }
}
