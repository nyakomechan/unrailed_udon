using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class TrackManager : UdonSharpBehaviour
{
    public const int MaxTrack = 512;
    public const int StarterLength = 30;
    public const int StarterBackLength = 7;
    public const float TileSize = 1f;
    public const int BridgeBit = 1 << 22;
    public const int RemovedBit = 1 << 23;

    [UdonSynced] public int[] trackData = new int[MaxTrack];
    [UdonSynced] public int trackLength;

    public Vector3 origin = Vector3.zero;

    public GameManager gameManager;
    public ChunkManager chunkManager;
    public TrainController trainController;
    public VRCObjectPool railPool;
    public ResourceSync resourceSync;
    public RailVisualTile[] railVisuals = new RailVisualTile[256];

    private int[] _pathTiles = new int[MaxTrack];
    private int[] _visX = new int[MaxTrack];
    private int[] _visZ = new int[MaxTrack];
    private int[] _inbound = new int[MaxTrack];
    private int[] _successor = new int[MaxTrack];
    private int _pathCount;
    private int _effectiveHead = -1;

    void Start()
    {
        ClaimOwnershipIfMaster();
        ApplyTrack();
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

    public int PackTile(int x, int z, int dir, int bridge)
    {
        return ((x + 512) << 12) | ((z + 512) << 2) | (dir & 3) | ((bridge & 1) << 22);
    }

    public int UnpackX(int packed) { return ((packed >> 12) & 1023) - 512; }
    public int UnpackZ(int packed) { return ((packed >> 2) & 1023) - 512; }
    public int UnpackDir(int packed) { return packed & 3; }
    public int UnpackBridge(int packed) { return (packed & BridgeBit) != 0 ? 1 : 0; }
    public bool IsRemoved(int packed) { return (packed & RemovedBit) != 0; }

    public Vector3 DirToVec(int dir)
    {
        if (dir == 0) return new Vector3(1f, 0f, 0f);
        if (dir == 1) return new Vector3(0f, 0f, 1f);
        if (dir == 2) return new Vector3(-1f, 0f, 0f);
        return new Vector3(0f, 0f, -1f);
    }

    public Vector3 TileToWorld(int x, int z)
    {
        return new Vector3(origin.x + x * TileSize, origin.y, origin.z + z * TileSize);
    }

    public void OwnerBuildStarterTrack()
    {
        if (!Networking.IsOwner(gameObject)) return;
        trackLength = StarterLength + StarterBackLength;
        for (int i = 0; i < StarterLength + StarterBackLength; i++)
        {
            trackData[i] = PackTile(i - StarterBackLength, 0, 0, 0);
        }
        RequestSerialization();
        ApplyTrack();
    }

    public float GetEndDistance()
    {
        return _pathCount - 0.5f;
    }

    public Vector3 GetPositionAt(float distance)
    {
        if (_pathCount <= 0) return origin;
        float d = Mathf.Clamp(distance, 0f, Mathf.Max(GetEndDistance(), 0f));
        int i = Mathf.Clamp(Mathf.FloorToInt(d + 0.5f), 0, _pathCount - 1);
        int packed = trackData[_pathTiles[i]];
        int x = UnpackX(packed);
        int z = UnpackZ(packed);
        int exitDir = UnpackDir(packed);
        int entryDir = i > 0 ? UnpackDir(trackData[_pathTiles[i - 1]]) : exitDir;
        float t = d - i;
        Vector3 center = TileToWorld(x, z);
        if (t < 0f) return center + DirToVec(entryDir) * t;
        return center + DirToVec(exitDir) * t;
    }

    public Vector3 GetTangentAt(float distance)
    {
        if (_pathCount <= 0) return Vector3.right;
        float d = Mathf.Clamp(distance, 0f, Mathf.Max(GetEndDistance(), 0f));
        int i = Mathf.Clamp(Mathf.FloorToInt(d + 0.5f), 0, _pathCount - 1);
        int exitDir = UnpackDir(trackData[_pathTiles[i]]);
        int entryDir = i > 0 ? UnpackDir(trackData[_pathTiles[i - 1]]) : exitDir;
        float t = d - i;
        if (t < 0f) return DirToVec(entryDir);
        return DirToVec(exitDir);
    }

    public override void OnDeserialization()
    {
        ApplyTrack();
    }

    public void ApplyTrack()
    {
        for (int i = 0; i < trackLength; i++)
        {
            _inbound[i] = -1;
            _successor[i] = -1;
        }
        for (int i = 0; i < trackLength; i++)
        {
            int p = trackData[i];
            if (IsRemoved(p)) continue;
            Vector3 v = DirToVec(UnpackDir(p));
            int nx = UnpackX(p) + Mathf.RoundToInt(v.x);
            int nz = UnpackZ(p) + Mathf.RoundToInt(v.z);
            for (int j = 0; j < trackLength; j++)
            {
                if (j == i) continue;
                int q = trackData[j];
                if (IsRemoved(q)) continue;
                if (UnpackX(q) == nx && UnpackZ(q) == nz)
                {
                    _successor[i] = j;
                    if (_inbound[j] < 0) _inbound[j] = i;
                    break;
                }
            }
        }

        _pathCount = 0;
        int cur = -1;
        for (int i = 0; i < trackLength; i++)
        {
            int p = trackData[i];
            if (IsRemoved(p)) continue;
            if (UnpackX(p) == 0 && UnpackZ(p) == 0) { cur = i; break; }
        }
        int visited = 0;
        while (cur >= 0 && _pathCount < MaxTrack)
        {
            int p = trackData[cur];
            int cx = UnpackX(p);
            int cz = UnpackZ(p);
            bool looped = false;
            for (int v = 0; v < visited; v++)
            {
                if (_visX[v] == cx && _visZ[v] == cz) { looped = true; break; }
            }
            if (looped) break;
            _visX[visited] = cx;
            _visZ[visited] = cz;
            visited++;
            _pathTiles[_pathCount] = cur;
            _pathCount++;
            cur = _successor[cur];
        }
        _effectiveHead = _pathCount > 0 ? _pathTiles[_pathCount - 1] : -1;
        ApplyVisuals();
    }

    private void ApplyVisuals()
    {
        for (int i = 0; i < railVisuals.Length; i++)
        {
            RailVisualTile rv = railVisuals[i];
            if (rv == null) continue;
            bool show = i < trackLength && !IsRemoved(trackData[i]);
            if (rv.gameObject.activeSelf != show) rv.gameObject.SetActive(show);
            if (!show) continue;
            int p = trackData[i];
            rv.tileIndex = i;
            int exitDir = UnpackDir(p);
            int entryDir;
            int pIdx = PathIndexOf(i);
            if (pIdx > 0)
            {
                entryDir = UnpackDir(trackData[_pathTiles[pIdx - 1]]);
            }
            else
            {
                int inb = _inbound[i];
                entryDir = inb >= 0 ? UnpackDir(trackData[inb]) : exitDir;
            }
            bool isCurve = entryDir != exitDir && entryDir != (exitDir + 2) % 4;

            rv.transform.position = TileToWorld(UnpackX(p), UnpackZ(p)) + new Vector3(0f, 0.03f, 0f);
            if (isCurve)
            {
                rv.transform.rotation = Quaternion.identity;
                ApplyCurveStrip(rv.curveA, DirToVec(entryDir) * -1f);
                ApplyCurveStrip(rv.curveB, DirToVec(exitDir));
            }
            else
            {
                rv.transform.rotation = Quaternion.LookRotation(DirToVec(exitDir));
            }
            if (rv.railL != null && rv.railL.activeSelf == isCurve) rv.railL.SetActive(!isCurve);
            if (rv.railR != null && rv.railR.activeSelf == isCurve) rv.railR.SetActive(!isCurve);
            if (rv.curveA != null && rv.curveA.activeSelf != isCurve) rv.curveA.SetActive(isCurve);
            if (rv.curveB != null && rv.curveB.activeSelf != isCurve) rv.curveB.SetActive(isCurve);
            if (rv.bridgeVisual != null && rv.bridgeVisual.activeSelf != (UnpackBridge(p) == 1))
            {
                rv.bridgeVisual.SetActive(UnpackBridge(p) == 1);
            }
        }
    }

    private void ApplyCurveStrip(GameObject strip, Vector3 armDir)
    {
        if (strip == null) return;
        strip.transform.localPosition = armDir * 0.25f + new Vector3(0f, 0.06f, 0f);
        bool alongX = Mathf.Abs(armDir.x) > 0.5f;
        strip.transform.localScale = alongX ? new Vector3(0.5f, 0.05f, 0.24f) : new Vector3(0.24f, 0.05f, 0.5f);
        strip.transform.localRotation = Quaternion.identity;
    }

    public bool IsInWindow(int tx)
    {
        if (chunkManager == null) return false;
        int baseTile = chunkManager.baseChunk * (int)ChunkManager.ChunkLength;
        return tx >= baseTile && tx < baseTile + (int)ChunkManager.ChunkLength * ChunkManager.ChunkCount;
    }

    public bool IsOccupied(int tx, int tz)
    {
        for (int i = 0; i < trackLength; i++)
        {
            int p = trackData[i];
            if (IsRemoved(p)) continue;
            if (UnpackX(p) == tx && UnpackZ(p) == tz) return true;
        }
        return false;
    }

    public int PathIndexOf(int arrayIndex)
    {
        for (int i = 0; i < _pathCount; i++)
        {
            if (_pathTiles[i] == arrayIndex) return i;
        }
        return -1;
    }

    public int GetHeadTilePacked()
    {
        return _effectiveHead >= 0 ? trackData[_effectiveHead] : -1;
    }

    public int FindRemovedIndexAt(int tx, int tz)
    {
        for (int i = 0; i < trackLength; i++)
        {
            int p = trackData[i];
            if (!IsRemoved(p)) continue;
            if (UnpackX(p) == tx && UnpackZ(p) == tz) return i;
        }
        return -1;
    }

    public int FindPresentIndexAt(int tx, int tz)
    {
        for (int i = 0; i < trackLength; i++)
        {
            int p = trackData[i];
            if (IsRemoved(p)) continue;
            if (UnpackX(p) == tx && UnpackZ(p) == tz) return i;
        }
        return -1;
    }

    public bool IsOnPath(int tx, int tz)
    {
        int idx = FindPresentIndexAt(tx, tz);
        return idx >= 0 && PathIndexOf(idx) >= 0;
    }

    public void OwnerBuildStationStub(int stationIndex)
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (chunkManager == null) return;
        int stationX = stationIndex * chunkManager.stationTiles;
        bool dirty = false;
        for (int i = 0; i < 2; i++)
        {
            int x = stationX - 1 + i;
            if (FindPresentIndexAt(x, 0) >= 0) continue;
            int removedIdx = FindRemovedIndexAt(x, 0);
            if (removedIdx >= 0)
            {
                trackData[removedIdx] &= ~RemovedBit;
                trackData[removedIdx] &= ~3;
                dirty = true;
                continue;
            }
            if (trackLength >= MaxTrack) break;
            trackData[trackLength] = PackTile(x, 0, 0, 0);
            trackLength++;
            dirty = true;
        }
        if (dirty)
        {
            RequestSerialization();
            ApplyTrack();
        }
    }

    private bool IsStationStubTile(int x, int z)
    {
        if (z != 0 || chunkManager == null) return false;
        int st = chunkManager.stationTiles;
        if (st <= 0) return false;
        int k = Mathf.RoundToInt((float)(x + 1) / st);
        if (k < 1) return false;
        return x == k * st - 1 || x == k * st;
    }

    public int GetDirIfRemovedAt(int tx, int tz)
    {
        int idx = FindRemovedIndexAt(tx, tz);
        if (idx < 0) return -1;
        int p = trackData[idx];
        int d = UnpackDir(p);
        Vector3 v = DirToVec(d);
        int nx = UnpackX(p) + Mathf.RoundToInt(v.x);
        int nz = UnpackZ(p) + Mathf.RoundToInt(v.z);
        if (FindPresentIndexAt(nx, nz) >= 0) return d;
        return -1;
    }

    public int GetAutoConnectDir(int x, int z, int dir)
    {
        Vector3 av = DirToVec(dir);
        int ax = x + Mathf.RoundToInt(av.x);
        int az = z + Mathf.RoundToInt(av.z);
        int tIdx = FindPresentIndexAt(ax, az);
        if (tIdx >= 0)
        {
            int tp = trackData[tIdx];
            Vector3 tv = DirToVec(UnpackDir(tp));
            if (UnpackX(tp) + Mathf.RoundToInt(tv.x) == x && UnpackZ(tp) + Mathf.RoundToInt(tv.z) == z)
            {
                return UnpackDir(tp);
            }
            return dir;
        }

        for (int i = 0; i < trackLength; i++)
        {
            int p = trackData[i];
            if (IsRemoved(p)) continue;
            int idir = UnpackDir(p);
            Vector3 iv = DirToVec(idir);
            if (UnpackX(p) + Mathf.RoundToInt(iv.x) != x || UnpackZ(p) + Mathf.RoundToInt(iv.z) != z) continue;
            if (idir == dir) continue;
            if (FindPresentIndexAt(x + Mathf.RoundToInt(iv.x), z + Mathf.RoundToInt(iv.z)) >= 0) return idir;
        }

        for (int i = 0; i < trackLength; i++)
        {
            int p = trackData[i];
            if (IsRemoved(p)) continue;
            if (PathIndexOf(i) >= 0) continue;
            if (_inbound[i] >= 0) continue;
            int nx2 = UnpackX(p);
            int nz2 = UnpackZ(p);
            Vector3 nv = DirToVec(UnpackDir(p));
            if (nx2 + Mathf.RoundToInt(nv.x) == x && nz2 + Mathf.RoundToInt(nv.z) == z) continue;
            int relX = nx2 - x;
            int relZ = nz2 - z;
            if (Mathf.Abs(relX) + Mathf.Abs(relZ) != 1) continue;
            return relX == 1 ? 0 : (relX == -1 ? 2 : (relZ == 1 ? 1 : 3));
        }
        return dir;
    }

    public bool HasInboundTo(int x, int z)
    {
        for (int i = 0; i < trackLength; i++)
        {
            int p = trackData[i];
            if (IsRemoved(p)) continue;
            int px = UnpackX(p);
            int pz = UnpackZ(p);
            Vector3 pv = DirToVec(UnpackDir(p));
            if (px + Mathf.RoundToInt(pv.x) == x && pz + Mathf.RoundToInt(pv.z) == z) return true;
        }
        return false;
    }

    public int CheckPlacement(int x, int z)
    {
        if (z < ChunkManager.TileZMin || z >= ChunkManager.TileZMin + ChunkManager.TileH) return 0;
        if (!IsInWindow(x)) return 0;
        if (IsOccupied(x, z))
        {
            int occIdx = FindPresentIndexAt(x, z);
            if (occIdx >= 0 && PathIndexOf(occIdx) < 0 && _effectiveHead >= 0)
            {
                int hp = trackData[_effectiveHead];
                int hx = UnpackX(hp);
                int hz = UnpackZ(hp);
                int hd = UnpackDir(hp);
                int relX = x - hx;
                int relZ = z - hz;
                if (Mathf.Abs(relX) + Mathf.Abs(relZ) == 1)
                {
                    int tdir = relX == 1 ? 0 : (relX == -1 ? 2 : (relZ == 1 ? 1 : 3));
                    if (tdir != (hd + 2) % 4)
                    {
                        if (tdir == hd) return 3;
                        if (trainController == null || trainController.trackDistance <= (_pathCount - 1) - 0.5f) return 3;
                    }
                }
            }
            return 0;
        }
        int nodeType = chunkManager.GetTypeAtTile(x, z);
        bool bridge = false;
        if (nodeType >= 0)
        {
            if (nodeType == 3) bridge = true;
            else return 0;
        }
        if (bridge && gameManager.woodCount < 1) return 0;
        if (_effectiveHead >= 0)
        {
            int hp2 = trackData[_effectiveHead];
            int hx2 = UnpackX(hp2);
            int hz2 = UnpackZ(hp2);
            int hd2 = UnpackDir(hp2);
            int relX2 = x - hx2;
            int relZ2 = z - hz2;
            if (Mathf.Abs(relX2) + Mathf.Abs(relZ2) == 1)
            {
                int tdir = relX2 == 1 ? 0 : (relX2 == -1 ? 2 : (relZ2 == 1 ? 1 : 3));
                if (tdir != hd2 && trainController != null && trainController.trackDistance > (_pathCount - 1) - 0.5f) return 0;
            }
        }
        return bridge ? 2 : 1;
    }

    [NetworkCallable]
    public void RequestPlaceRail(int pack, int railIndex)
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (railPool == null) return;
        if (railIndex < 0 || railIndex >= railPool.Pool.Length) return;
        GameObject railObj = railPool.Pool[railIndex];
        if (railObj == null || !railObj.activeSelf) return;
        if (NetworkCalling.InNetworkCall)
        {
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (caller == null || !caller.IsValid()) return;
            VRCPlayerApi railOwner = Networking.GetOwner(railObj);
            if (railOwner == null || !railOwner.IsValid() || railOwner.playerId != caller.playerId) return;
        }

        int x = UnpackX(pack);
        int z = UnpackZ(pack);
        int aimDir = UnpackDir(pack);
        int code = CheckPlacement(x, z);
        if (code == 0) return;
        if (code == 3)
        {
            ApplyConnectionRewrites(x, z, -1);
            RequestSerialization();
            ApplyTrack();
            return;
        }
        int bridge = code == 2 ? 1 : 0;
        if (bridge == 1 && !gameManager.OwnerConsumeWood(1)) return;

        int removedIdx = FindRemovedIndexAt(x, z);
        if (removedIdx >= 0)
        {
            int stored = trackData[removedIdx];
            int sdir = UnpackDir(stored);
            Vector3 sv = DirToVec(sdir);
            int snx = UnpackX(stored) + Mathf.RoundToInt(sv.x);
            int snz = UnpackZ(stored) + Mathf.RoundToInt(sv.z);
            if (FindPresentIndexAt(snx, snz) < 0)
            {
                trackData[removedIdx] = (stored & ~3) | (aimDir & 3);
            }
            trackData[removedIdx] &= ~RemovedBit;
        }
        else
        {
            if (trackLength >= MaxTrack) return;
            aimDir = GetAutoConnectDir(x, z, aimDir);
            trackData[trackLength] = PackTile(x, z, aimDir, bridge);
            trackLength++;
        }
        ApplyConnectionRewrites(x, z, removedIdx >= 0 ? removedIdx : trackLength - 1);
        railPool.Return(railObj);
        if (resourceSync != null) resourceSync.OwnerNoteReturn(2, railIndex);
        RequestSerialization();
        ApplyTrack();
    }

    private void ApplyConnectionRewrites(int x, int z, int newIdx)
    {
        if (_effectiveHead >= 0)
        {
            int hp = trackData[_effectiveHead];
            int hx = UnpackX(hp);
            int hz = UnpackZ(hp);
            int hd = UnpackDir(hp);
            int relX = x - hx;
            int relZ = z - hz;
            if (Mathf.Abs(relX) + Mathf.Abs(relZ) == 1)
            {
                int tdir = relX == 1 ? 0 : (relX == -1 ? 2 : (relZ == 1 ? 1 : 3));
                if (tdir != hd) trackData[_effectiveHead] = (hp & ~3) | tdir;
            }
        }

        int newTargetX = int.MinValue;
        int newTargetZ = int.MinValue;
        if (newIdx >= 0)
        {
            int np = trackData[newIdx];
            Vector3 nv = DirToVec(UnpackDir(np));
            newTargetX = UnpackX(np) + Mathf.RoundToInt(nv.x);
            newTargetZ = UnpackZ(np) + Mathf.RoundToInt(nv.z);
        }

        for (int i = 0; i < trackLength; i++)
        {
            int p = trackData[i];
            if (IsRemoved(p)) continue;
            if (PathIndexOf(i) >= 0) continue;
            int succ = _successor[i];
            if (succ >= 0 && _successor[succ] != i) continue;
            int nx = UnpackX(p);
            int nz = UnpackZ(p);
            if (nx == newTargetX && nz == newTargetZ) continue;
            int relX = x - nx;
            int relZ = z - nz;
            if (Mathf.Abs(relX) + Mathf.Abs(relZ) != 1) continue;
            int tdir = relX == 1 ? 0 : (relX == -1 ? 2 : (relZ == 1 ? 1 : 3));
            int nd = UnpackDir(p);
            if (tdir == nd) continue;
            if (tdir == (nd + 2) % 4) continue;
            trackData[i] = (p & ~3) | tdir;
        }
    }

    [NetworkCallable]
    public void RequestRemoveRail(int tileIndex)
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (tileIndex < 0 || tileIndex >= trackLength) return;
        if (IsRemoved(trackData[tileIndex])) return;
        if (UnpackX(trackData[tileIndex]) < 0) return;
        if (IsStationStubTile(UnpackX(trackData[tileIndex]), UnpackZ(trackData[tileIndex]))) return;
        int pIdx = PathIndexOf(tileIndex);
        if (pIdx >= 0 && trainController != null && pIdx <= Mathf.FloorToInt(trainController.trackDistance + 0.5f)) return;

        Vector3 pos = TileToWorld(UnpackX(trackData[tileIndex]), UnpackZ(trackData[tileIndex])) + new Vector3(0f, 0.4f, 0f);
        GameObject g = OwnerSpawnRailItem(pos);
        if (g == null) return;

        trackData[tileIndex] |= RemovedBit;
        RevertDirsPointingAt(UnpackX(trackData[tileIndex]), UnpackZ(trackData[tileIndex]));
        RequestSerialization();
        ApplyTrack();
    }

    private void RevertDirsPointingAt(int tx, int tz)
    {
        for (int i = 0; i < trackLength; i++)
        {
            int p = trackData[i];
            if (IsRemoved(p)) continue;
            int ux = UnpackX(p);
            int uz = UnpackZ(p);
            int ud = UnpackDir(p);
            Vector3 v = DirToVec(ud);
            if (ux + Mathf.RoundToInt(v.x) != tx || uz + Mathf.RoundToInt(v.z) != tz) continue;

            int entry = -1;
            int inb = _inbound[i];
            if (inb >= 0) entry = UnpackDir(trackData[inb]);
            if (entry >= 0 && entry != ud) trackData[i] = (p & ~3) | entry;
        }
    }

    public GameObject OwnerSpawnRailItem(Vector3 pos)
    {
        if (!Networking.IsOwner(gameObject)) return null;
        if (railPool == null) return null;
        GameObject g = railPool.TryToSpawn();
        if (g == null) return null;
        g.transform.position = pos;
        if (resourceSync != null)
        {
            for (int i = 0; i < railPool.Pool.Length; i++)
            {
                if (railPool.Pool[i] == g) { resourceSync.OwnerNoteSpawn(2, i, pos); break; }
            }
        }
        return g;
    }

    public void OwnerReturnAllRails()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (railPool == null) return;
        foreach (GameObject g in railPool.Pool)
        {
            if (g != null && g.activeSelf) railPool.Return(g);
        }
        if (resourceSync != null) resourceSync.OwnerNoteReturnAll();
    }
}
