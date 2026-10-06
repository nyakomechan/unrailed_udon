using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class RailItemBehaviour : UdonSharpBehaviour
{
    public TrackManager trackManager;
    public ResourceSync resourceSync;
    public Transform ghostVisual;
    public Renderer ghostRenderer;
    public Material validMaterial;
    public Material invalidMaterial;
    public float reach = 6f;

    private VRCPickup _pickup;
    private int _lastX;
    private int _lastZ;
    private int _lastDir;
    private bool _aimValid;

    private void EnsureInit()
    {
        if (_pickup == null) _pickup = GetComponent<VRCPickup>();
    }

    void Update()
    {
        EnsureInit();
        bool show = false;
        if (_pickup != null && _pickup.IsHeld && Networking.IsOwner(gameObject) && trackManager != null)
        {
            Ray ray = new Ray(transform.position, transform.forward);
            Vector3 hitPoint;
            if (AimRaycast(ray, out hitPoint))
            {
                int tx = Mathf.FloorToInt(hitPoint.x - trackManager.origin.x + 0.5f);
                int tz = Mathf.FloorToInt(hitPoint.z - trackManager.origin.z + 0.5f);
                int dir = YawToDir(transform.eulerAngles.y);
                int forcedDir = trackManager.GetDirIfRemovedAt(tx, tz);
                if (forcedDir >= 0) dir = forcedDir;
                else dir = trackManager.GetAutoConnectDir(tx, tz, dir);
                int code = trackManager.CheckPlacement(tx, tz);
                _lastX = tx;
                _lastZ = tz;
                _lastDir = dir;
                _aimValid = code > 0;
                if (ghostVisual != null)
                {
                    ghostVisual.position = trackManager.TileToWorld(tx, tz) + new Vector3(0f, 0.12f, 0f);
                    ghostVisual.rotation = Quaternion.LookRotation(trackManager.DirToVec(dir));
                }
                if (ghostRenderer != null && validMaterial != null && invalidMaterial != null)
                {
                    ghostRenderer.sharedMaterial = _aimValid ? validMaterial : invalidMaterial;
                }
                show = true;
            }
        }
        if (ghostVisual != null && ghostVisual.gameObject.activeSelf != show)
        {
            ghostVisual.gameObject.SetActive(show);
        }
        if (!show) _aimValid = false;
    }

    public override void OnPickupUseDown()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (!_aimValid) return;
        int railIndex = FindRailIndex();
        if (railIndex < 0) return;
        int pack = trackManager.PackTile(_lastX, _lastZ, _lastDir, 0);
        trackManager.SendCustomNetworkEvent(NetworkEventTarget.Owner, "RequestPlaceRail", pack, railIndex);
    }

    public override void OnPickup()
    {
        if (resourceSync == null) return;
        VRCPlayerApi lp = Networking.LocalPlayer;
        if (lp == null) return;
        int railIndex = FindRailIndex();
        if (railIndex < 0) return;
        int handBit = 0;
        VRCPickup pk = (VRCPickup)GetComponent(typeof(VRCPickup));
        if (pk != null && pk.currentHand == VRC_Pickup.PickupHand.Right) handBit = 1;
        resourceSync.SendCustomNetworkEvent(NetworkEventTarget.Owner, "RequestSetHolder", 2, railIndex, (lp.playerId << 1) | handBit);
    }

    public override void OnDrop()
    {
        if (resourceSync == null) return;
        int railIndex = FindRailIndex();
        if (railIndex < 0) return;
        Vector3 p = transform.position;
        resourceSync.SendCustomNetworkEvent(NetworkEventTarget.Owner, "RequestDrop", 2, railIndex, p.x, p.z);
    }

    private int FindRailIndex()
    {
        if (trackManager == null || trackManager.railPool == null) return -1;
        GameObject[] pool = trackManager.railPool.Pool;
        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i] == gameObject) return i;
        }
        return -1;
    }

    private int YawToDir(float yaw)
    {
        yaw = (yaw % 360f + 360f) % 360f;
        if (yaw >= 315f || yaw < 45f) return 1;
        if (yaw < 135f) return 0;
        if (yaw < 225f) return 3;
        return 2;
    }

    private bool AimRaycast(Ray ray, out Vector3 point)
    {
        point = Vector3.zero;
        RaycastHit[] hits = Physics.RaycastAll(ray, reach, 1, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider.GetComponentInParent<VRCPickup>() != null) continue;
            if (hits[i].distance < best)
            {
                best = hits[i].distance;
                point = hits[i].point;
                found = true;
            }
        }
        return found;
    }
}

