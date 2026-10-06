using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ResourceSync : UdonSharpBehaviour
{
    public const int WoodCount = 48;
    public const int IronCount = 32;
    public const int RailCount = 16;
    public const float GroundY = 0.6f;

    public VRCObjectPool woodPool;
    public VRCObjectPool ironPool;
    public VRCObjectPool railPool;

    [UdonSynced] public Vector3[] woodPos = new Vector3[WoodCount];
    [UdonSynced] public Vector3[] ironPos = new Vector3[IronCount];
    [UdonSynced] public Vector3[] railPos = new Vector3[RailCount];
    [UdonSynced] public int[] woodHolder = new int[WoodCount];
    [UdonSynced] public int[] ironHolder = new int[IronCount];
    [UdonSynced] public int[] railHolder = new int[RailCount];

    void Start()
    {
        ClaimOwnershipIfMaster();
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

    public void OwnerNoteSpawn(int itemType, int slot, Vector3 pos)
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (itemType == 0)
        {
            if (slot < 0 || slot >= woodPos.Length) return;
            woodPos[slot] = pos;
            woodHolder[slot] = 0;
        }
        else if (itemType == 1)
        {
            if (slot < 0 || slot >= ironPos.Length) return;
            ironPos[slot] = pos;
            ironHolder[slot] = 0;
        }
        else
        {
            if (slot < 0 || slot >= railPos.Length) return;
            railPos[slot] = pos;
            railHolder[slot] = 0;
        }
        ApplyPositions();
        RequestSerialization();
    }

    public void OwnerNoteReturn(int itemType, int slot)
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (itemType == 0)
        {
            if (slot < 0 || slot >= woodHolder.Length) return;
            woodHolder[slot] = 0;
        }
        else if (itemType == 1)
        {
            if (slot < 0 || slot >= ironHolder.Length) return;
            ironHolder[slot] = 0;
        }
        else
        {
            if (slot < 0 || slot >= railHolder.Length) return;
            railHolder[slot] = 0;
        }
        RequestSerialization();
    }

    public void OwnerNoteReturnAll()
    {
        if (!Networking.IsOwner(gameObject)) return;
        for (int i = 0; i < woodHolder.Length; i++) woodHolder[i] = 0;
        for (int i = 0; i < ironHolder.Length; i++) ironHolder[i] = 0;
        for (int i = 0; i < railHolder.Length; i++) railHolder[i] = 0;
        RequestSerialization();
    }

    [NetworkCallable]
    public void RequestSetHolder(int itemType, int slot, int holderPack)
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (itemType == 0)
        {
            if (slot < 0 || slot >= woodHolder.Length) return;
            woodHolder[slot] = holderPack;
        }
        else if (itemType == 1)
        {
            if (slot < 0 || slot >= ironHolder.Length) return;
            ironHolder[slot] = holderPack;
        }
        else
        {
            if (slot < 0 || slot >= railHolder.Length) return;
            railHolder[slot] = holderPack;
        }
        RequestSerialization();
    }

    [NetworkCallable]
    public void RequestDrop(int itemType, int slot, float x, float z)
    {
        if (!Networking.IsOwner(gameObject)) return;
        Vector3 p = new Vector3(x, GroundY, z);
        if (itemType == 0)
        {
            if (slot < 0 || slot >= woodPos.Length) return;
            woodHolder[slot] = 0;
            woodPos[slot] = p;
        }
        else if (itemType == 1)
        {
            if (slot < 0 || slot >= ironPos.Length) return;
            ironHolder[slot] = 0;
            ironPos[slot] = p;
        }
        else
        {
            if (slot < 0 || slot >= railPos.Length) return;
            railHolder[slot] = 0;
            railPos[slot] = p;
        }
        ApplyPositions();
        RequestSerialization();
    }

    public bool IsHeldSynced(int itemType, int slot)
    {
        if (itemType == 0) return slot >= 0 && slot < woodHolder.Length && woodHolder[slot] != 0;
        if (itemType == 1) return slot >= 0 && slot < ironHolder.Length && ironHolder[slot] != 0;
        return slot >= 0 && slot < railHolder.Length && railHolder[slot] != 0;
    }

    public override void OnDeserialization()
    {
        ApplyPositions();
    }

    private void ApplyPositions()
    {
        ApplyFor(woodPool, woodPos, woodHolder);
        ApplyFor(ironPool, ironPos, ironHolder);
        ApplyFor(railPool, railPos, railHolder);
    }

    private void ApplyFor(VRCObjectPool pool, Vector3[] pos, int[] holder)
    {
        if (pool == null) return;
        int n = pool.Pool.Length;
        if (n > pos.Length) n = pos.Length;
        for (int i = 0; i < n; i++)
        {
            GameObject g = pool.Pool[i];
            if (g == null || !g.activeSelf) continue;
            if (holder[i] != 0) continue;
            g.transform.position = pos[i];
        }
    }

    void LateUpdate()
    {
        FollowHeld(woodPool, woodPos, woodHolder);
        FollowHeld(ironPool, ironPos, ironHolder);
        FollowHeld(railPool, railPos, railHolder);
    }

    private void FollowHeld(VRCObjectPool pool, Vector3[] pos, int[] holder)
    {
        if (pool == null) return;
        int n = pool.Pool.Length;
        if (n > holder.Length) n = holder.Length;
        for (int i = 0; i < n; i++)
        {
            int h = holder[i];
            if (h == 0) continue;
            GameObject g = pool.Pool[i];
            if (g == null || !g.activeSelf) continue;
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(h >> 1);
            if (p == null || !p.IsValid())
            {
                if (Networking.IsOwner(gameObject))
                {
                    pos[i] = g.transform.position;
                    holder[i] = 0;
                    RequestSerialization();
                }
                continue;
            }
            if (p.isLocal) continue;
            VRCPlayerApi.TrackingData td = p.GetTrackingData((h & 1) == 1
                ? VRCPlayerApi.TrackingDataType.RightHand
                : VRCPlayerApi.TrackingDataType.LeftHand);
            g.transform.position = td.position;
        }
    }
}
