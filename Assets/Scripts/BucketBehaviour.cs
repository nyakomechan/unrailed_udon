using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
public class BucketBehaviour : UdonSharpBehaviour
{
    public int bucketIndex;
    public BucketManager bucketManager;
    public float useCooldown = 0.4f;
    public float scoopReach = 1.4f;
    public float pourReach = 1.8f;

    private float _lastUse = -999f;
    public int dbgStage;

    public override void OnPickupUseDown()
    {
        dbgStage = 1;
        if (!Networking.IsOwner(gameObject)) return;
        dbgStage = 2;
        if (bucketManager == null) return;
        dbgStage = 3;
        if (Time.time - _lastUse < useCooldown) return;

        if (bucketManager.IsFull(bucketIndex))
        {
            dbgStage = 4;
            if (IsColliderNamedNear("WaterTank", pourReach))
            {
                _lastUse = Time.time;
                bucketManager.SendCustomNetworkEvent(NetworkEventTarget.Owner, "RequestPour", bucketIndex);
                dbgStage = 5;
            }
            return;
        }

        dbgStage = 6;
        if (bucketManager.chunkManager == null) return;
        dbgStage = 7;
        Collider[] near = Physics.OverlapSphere(transform.position, scoopReach, 1, QueryTriggerInteraction.Ignore);
        dbgStage = 800 + near.Length;
        for (int i = 0; i < near.Length; i++)
        {
            Collider c = near[i];
            if (c == null || !c.name.StartsWith("Node")) continue;
            Vector3 p = c.transform.position;
            int tx = Mathf.FloorToInt(p.x - bucketManager.chunkManager.origin.x + 0.5f);
            int tz = Mathf.FloorToInt(p.z - bucketManager.chunkManager.origin.z + 0.5f);
            if (bucketManager.chunkManager.GetTypeAtTile(tx, tz) == 3)
            {
                _lastUse = Time.time;
                bucketManager.SendCustomNetworkEvent(NetworkEventTarget.Owner, "RequestScoop", bucketIndex);
                dbgStage = 9;
                break;
            }
        }
    }

    private bool IsColliderNamedNear(string targetName, float r)
    {
        Collider[] near = Physics.OverlapSphere(transform.position, r, 1, QueryTriggerInteraction.Collide);
        for (int i = 0; i < near.Length; i++)
        {
            if (near[i] != null && near[i].name == targetName) return true;
        }
        return false;
    }
}
