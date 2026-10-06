using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ChunkManager : UdonSharpBehaviour
{
    public const int ChunkCount = 6;
    public const float ChunkLength = 24f;
    public const int TileW = 24;
    public const int TileH = 22;
    public const int TilesPerChunk = TileW * TileH;
    public const int TileZMin = -12;
    public const int MaskIntsPerChunk = 17;

    [Header("Generation tuning")]
    public int corridorChunks = 2;
    public int maxAttempt = 3;
    public int stationTiles = 30;
    public int noiseOffsetCm = 10000;
    public int wlTreeX = 1200, wlTreeZ = 900;
    public int wlRockX = 1000, wlRockZ = 800;
    public int wlWaterX = 1000, wlWaterZ = 1000;

    [Header("Pool / allocation")]
    public int poolBudgetPerChunk = 192;
    public int hardCapPercent = 33;

    [Header("Harvest")]
    public int treeHp = 2;
    public int rockHp = 4;

    [Header("Debug (-1 = use tier curve)")]
    public int debugForceTier = -1;

    private const uint LayerTree = 0x9E3779B9u;
    private const uint LayerRock = 0x3C6EF372u;
    private const uint LayerWater = 0x7F4A7C15u;

    public int[] ThrWater = { 842, 813, 783, 757, 730, 730, 730, 730, 730 };
    public int[] ThrRock = { 708, 695, 682, 673, 664, 658, 658, 658, 658 };
    public int[] ThrTree = { 595, 592, 588, 584, 580, 577, 573, 569, 566 };
    public int[] ThrHard = {
        848, 887, 932, 1001, 822, 866, 900, 1001, 796, 848, 887, 1001,
        773, 835, 877, 1001, 754, 822, 866, 1001, 741, 813, 859, 1001,
        741, 813, 859, 1001, 741, 813, 859, 1001, 741, 813, 859, 1001
    };

    private readonly int[] GradX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private readonly int[] GradZ = { 0, 1, 1, 1, 0, -1, -1, -1 };

    [UdonSynced] public int[] harvestMask = new int[ChunkCount * MaskIntsPerChunk];
    [UdonSynced] public int baseChunk;
    [UdonSynced] public int attemptPacked;

    public GameManager gameManager;
    public TrackManager trackManager;
    public TrainController trainController;
    public Transform[] chunkRoots = new Transform[ChunkCount];
    public Transform[] nodeRoots = new Transform[0];
    public VRCObjectPool woodPool;
    public VRCObjectPool ironPool;
    public ResourceSync resourceSync;
    public Vector3 origin = Vector3.zero;

    private int[] _tileTypes = new int[ChunkCount * TilesPerChunk];
    private int[] _hp = new int[ChunkCount * TilesPerChunk];
    private bool[] _allocated = new bool[ChunkCount * TilesPerChunk];
    private int[] _tileToPool = new int[ChunkCount * TilesPerChunk];
    private int[] _appliedMask = new int[ChunkCount * MaskIntsPerChunk];
    private int[] _loadedChunk = new int[ChunkCount];
    private int _appliedSeed = -1;
    private int _nodesPerChunk;

    private Transform[] _visT;
    private Transform[] _visR;
    private Transform[] _visH;
    private Transform[] _visP;

    private const int BfsW = ChunkCount * TileW;
    private const int BfsH = TileH;
    private bool[] _bfsBlocked = new bool[BfsW * BfsH];
    private bool[] _bfsVisited = new bool[BfsW * BfsH];
    private int[] _bfsQueue = new int[BfsW * BfsH];
    private int _bfsMaxX;

    void Start()
    {
        for (int i = 0; i < ChunkCount; i++) _loadedChunk[i] = -999999;
        _nodesPerChunk = nodeRoots.Length / ChunkCount;
        int n = nodeRoots.Length;
        _visT = new Transform[n];
        _visR = new Transform[n];
        _visH = new Transform[n];
        _visP = new Transform[n];
        for (int i = 0; i < n; i++)
        {
            Transform r = nodeRoots[i];
            if (r == null) continue;
            _visT[i] = r.Find("T");
            _visR[i] = r.Find("R");
            _visH[i] = r.Find("H");
            _visP[i] = r.Find("P");
            if (r.gameObject.activeSelf) r.gameObject.SetActive(false);
        }
        ClaimOwnershipIfMaster();
        if (Networking.IsOwner(gameObject))
        {
            OwnerResetChunks();
        }
        ApplyChunks();
    }

    private void ClaimOwnershipIfMaster()
    {
        if (!Networking.IsMaster) return;
        if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        if (woodPool != null && !Networking.IsOwner(woodPool.gameObject)) Networking.SetOwner(Networking.LocalPlayer, woodPool.gameObject);
        if (ironPool != null && !Networking.IsOwner(ironPool.gameObject)) Networking.SetOwner(Networking.LocalPlayer, ironPool.gameObject);
    }

    public override void OnMasterTransferred(VRCPlayerApi newMaster)
    {
        ClaimOwnershipIfMaster();
    }

    public int WorldChunkForSlot(int slot)
    {
        return baseChunk + (((slot - baseChunk) % ChunkCount) + ChunkCount) % ChunkCount;
    }

    void Update()
    {
        if (gameManager != null)
        {
            int s = gameManager.runSeed;
            if (s != 0 && s != _appliedSeed) ApplyChunks();
        }
        if (!Networking.IsOwner(gameObject)) return;
        if (trackManager == null || trackManager.trackLength <= 0) return;
        if (trainController == null) return;

        int ti = Mathf.Clamp(Mathf.FloorToInt(trainController.trackDistance + 0.5f), 0, trackManager.trackLength - 1);
        float trainX = trackManager.UnpackX(trackManager.trackData[ti]) + trackManager.origin.x;
        int trainChunk = Mathf.FloorToInt(trainX / ChunkLength);
        while (baseChunk < trainChunk - 1)
        {
            OwnerRecycleOldestChunk();
        }
    }

    public int GetTypeAtTile(int tx, int tz)
    {
        int wc = Mathf.FloorToInt((float)tx / ChunkLength);
        if (wc < baseChunk || wc >= baseChunk + ChunkCount) return -1;
        int slot = ((wc % ChunkCount) + ChunkCount) % ChunkCount;
        int lx = tx - wc * TileW;
        int lz = tz - TileZMin;
        if (lx < 0 || lx >= TileW || lz < 0 || lz >= TileH) return -1;
        int idx = slot * TilesPerChunk + lx + lz * TileW;
        int t = _tileTypes[idx];
        if (t < 0) return -1;
        if (MaskBit(slot, lx + lz * TileW)) return -1;
        if (!_allocated[idx]) return -1;
        return t;
    }

    public void OwnerRecycleOldestChunk()
    {
        if (!Networking.IsOwner(gameObject)) return;
        int slot = ((baseChunk % ChunkCount) + ChunkCount) % ChunkCount;
        baseChunk++;
        for (int j = 0; j < MaskIntsPerChunk; j++) harvestMask[slot * MaskIntsPerChunk + j] = 0;
        SetAttempt(slot, 0);
        ValidateWindow();
        RequestSerialization();
        ApplyChunks();
    }

    public void OwnerResetChunks()
    {
        if (!Networking.IsOwner(gameObject)) return;
        baseChunk = 0;
        attemptPacked = 0;
        for (int i = 0; i < harvestMask.Length; i++) harvestMask[i] = 0;
        ValidateWindow();
        RequestSerialization();
        ApplyChunks();
    }

    public void OwnerReturnAllPickups()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (woodPool != null)
        {
            foreach (GameObject g in woodPool.Pool) if (g != null && g.activeSelf) woodPool.Return(g);
        }
        if (ironPool != null)
        {
            foreach (GameObject g in ironPool.Pool) if (g != null && g.activeSelf) ironPool.Return(g);
        }
        if (resourceSync != null) resourceSync.OwnerNoteReturnAll();
    }

    public void OwnerReturnPickup(int itemType, GameObject go)
    {
        if (!Networking.IsOwner(gameObject)) return;
        VRCObjectPool pool = itemType == 0 ? woodPool : ironPool;
        if (pool == null || go == null) return;
        pool.Return(go);
        if (resourceSync != null)
        {
            GameObject[] arr = pool.Pool;
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] == go) { resourceSync.OwnerNoteReturn(itemType, i); break; }
            }
        }
    }

    [NetworkCallable(maxEventsPerSecond: 30)]
    public void RequestHarvestTile(int tx, int tz, int toolType)
    {
        if (!Networking.IsOwner(gameObject)) return;
        int wc = Mathf.FloorToInt((float)tx / ChunkLength);
        if (wc < baseChunk || wc >= baseChunk + ChunkCount) return;
        int slot = ((wc % ChunkCount) + ChunkCount) % ChunkCount;
        int lx = tx - wc * TileW;
        int lz = tz - TileZMin;
        if (lx < 0 || lx >= TileW || lz < 0 || lz >= TileH) return;
        int t = lx + lz * TileW;
        int idx = slot * TilesPerChunk + t;
        int type = _tileTypes[idx];
        if (type != 0 && type != 1) return;
        if (type != toolType) return;
        if (!_allocated[idx]) return;
        if (MaskBit(slot, t)) return;

        _hp[idx]--;
        if (_hp[idx] > 0) return;

        harvestMask[slot * MaskIntsPerChunk + t / 32] |= 1 << (t % 32);
        RequestSerialization();
        ApplyHarvestVisuals();
        SpawnResourceAt(type, tx, tz);
    }

    private void SpawnResourceAt(int type, int tx, int tz)
    {
        VRCObjectPool pool = type == 0 ? woodPool : ironPool;
        if (pool == null) return;
        GameObject g = pool.TryToSpawn();
        if (g == null) return;
        Vector3 pos = new Vector3(origin.x + tx, 0.6f, origin.z + tz);
        g.transform.position = pos;
        if (resourceSync != null)
        {
            GameObject[] arr = pool.Pool;
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] == g) { resourceSync.OwnerNoteSpawn(type, i, pos); break; }
            }
        }
    }

    public override void OnDeserialization()
    {
        ApplyChunks();
    }

    public void _DebugForceRegen()
    {
        _appliedSeed = -1;
        for (int i = 0; i < ChunkCount; i++) _loadedChunk[i] = -999999;
        for (int i = 0; i < _appliedMask.Length; i++) _appliedMask[i] = 0;
        ApplyChunks();
        ApplyHarvestVisuals();
    }

    public void ApplyChunks()
    {
        if (gameManager == null) return;
        int seed = gameManager.runSeed;
        if (seed == 0) return;

        bool seedChanged = seed != _appliedSeed;
        for (int slot = 0; slot < ChunkCount; slot++)
        {
            int w = WorldChunkForSlot(slot);
            if (seedChanged || _loadedChunk[slot] != w)
            {
                GenerateChunk(slot, w, seed);
                _loadedChunk[slot] = w;
            }
        }
        _appliedSeed = seed;
        for (int i = 0; i < _appliedMask.Length; i++) _appliedMask[i] = 0;
        ApplyHarvestVisuals();
    }

    private void ApplyHarvestVisuals()
    {
        for (int slot = 0; slot < ChunkCount; slot++)
        {
            int mb = slot * MaskIntsPerChunk;
            for (int j = 0; j < MaskIntsPerChunk; j++)
            {
                int diff = harvestMask[mb + j] ^ _appliedMask[mb + j];
                if (diff == 0) continue;
                for (int b = 0; b < 32; b++)
                {
                    if ((diff & (1 << b)) == 0) continue;
                    int t = j * 32 + b;
                    if (t >= TilesPerChunk) break;
                    int poolIdx = _tileToPool[slot * TilesPerChunk + t];
                    if (poolIdx >= 0)
                    {
                        Transform node = nodeRoots[poolIdx];
                        if (node != null && node.gameObject.activeSelf) node.gameObject.SetActive(false);
                    }
                }
                _appliedMask[mb + j] = harvestMask[mb + j];
            }
        }
    }

    private bool MaskBit(int slot, int t)
    {
        return (harvestMask[slot * MaskIntsPerChunk + t / 32] & (1 << (t % 32))) != 0;
    }

    // ---------- 生成 (1mタイル, 固定小数点Perlin) ----------

    private int GetAttempt(int slot)
    {
        return (attemptPacked >> (slot * 2)) & 3;
    }

    private void SetAttempt(int slot, int v)
    {
        attemptPacked = (attemptPacked & ~(3 << (slot * 2))) | ((v & 3) << (slot * 2));
    }

    private int TierOf(int worldChunk)
    {
        if (debugForceTier >= 0) return debugForceTier > 8 ? 8 : debugForceTier;
        int t = worldChunk * TileW / stationTiles;
        return t > 8 ? 8 : t;
    }

    private uint NoiseHash(int ix, int iz, uint s)
    {
        uint h = (uint)ix * 374761393u + (uint)iz * 668265263u + s * 974711u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }

    private int NoiseFade(int t)
    {
        long t2 = ((long)t * t) >> 10;
        long t3 = (t2 * t) >> 10;
        return (int)((t3 * (6 * t2 - 15 * t + 10240)) >> 10);
    }

    private int Noise1000(int px, int pz, int wlx, int wlz, uint s)
    {
        long gx = ((long)px * 1024) / wlx;
        long gz = ((long)pz * 1024) / wlz;
        int ix = (int)(gx >> 10);
        int fx = (int)(gx & 1023);
        int iz = (int)(gz >> 10);
        int fz = (int)(gz & 1023);
        int g = (int)(NoiseHash(ix, iz, s) & 7u);
        int d00 = GradX[g] * fx + GradZ[g] * fz;
        g = (int)(NoiseHash(ix + 1, iz, s) & 7u);
        int d10 = GradX[g] * (fx - 1024) + GradZ[g] * fz;
        g = (int)(NoiseHash(ix, iz + 1, s) & 7u);
        int d01 = GradX[g] * fx + GradZ[g] * (fz - 1024);
        g = (int)(NoiseHash(ix + 1, iz + 1, s) & 7u);
        int d11 = GradX[g] * (fx - 1024) + GradZ[g] * (fz - 1024);
        int u = NoiseFade(fx);
        int v = NoiseFade(fz);
        int nx0 = d00 + ((u * (d10 - d00)) >> 10);
        int nx1 = d01 + ((v * (d11 - d01)) >> 10);
        int n = nx0 + ((v * (nx1 - nx0)) >> 10);
        int val = ((n + 724) * 1000) / 1448;
        return val < 0 ? 0 : (val > 1000 ? 1000 : val);
    }

    private int TileJitter(int wxTile, int wzTile, int seed, int axis)
    {
        uint h = NoiseHash(wxTile + 8192, wzTile + 8192, (uint)seed ^ (axis == 0 ? 0xA53A9D01u : 0x5F3759DFu));
        return (int)(h & 63u) - 32;
    }

    private int ComputeTileType(int worldChunk, int seed, int attempt, int lx, int lz)
    {
        int originZTile = Mathf.RoundToInt(origin.z);
        int wzTile = TileZMin + lz + originZTile;
        if (worldChunk < corridorChunks && Mathf.Abs(wzTile - originZTile) < 2) return -1;

        int wxTile0 = worldChunk * TileW + lx;
        int stationX = Mathf.RoundToInt((float)wxTile0 / stationTiles) * stationTiles;
        if (stationX > 0 && Mathf.Abs(wxTile0 - stationX) <= 2 && Mathf.Abs(wzTile - originZTile) <= 3) return -1;

        int wxTile = worldChunk * TileW + lx;
        int pxCenter = wxTile * 100 + noiseOffsetCm;
        int pzCenter = wzTile * 100 + noiseOffsetCm;
        int px = pxCenter + TileJitter(wxTile, wzTile, seed, 0);
        int pz = pzCenter + TileJitter(wxTile, wzTile, seed, 1);
        int tier = TierOf(worldChunk);
        uint ls = (uint)seed;
        int rockV = Noise1000(px, pz, wlRockX, wlRockZ, ls ^ LayerRock);
        if (Noise1000(pxCenter, pzCenter, wlWaterX, wlWaterZ, ls ^ LayerWater) >= ThrWater[tier]) return 3;
        if (rockV >= ThrHard[tier * 4 + attempt]) return 2;
        if (rockV >= ThrRock[tier]) return 1;
        if (Noise1000(px, pz, wlTreeX, wlTreeZ, ls ^ LayerTree) >= ThrTree[tier]) return 0;
        return -1;
    }

    private void GenerateChunk(int slot, int worldChunk, int seed)
    {
        Transform root = chunkRoots[slot];
        if (root == null) return;
        root.position = new Vector3(origin.x + worldChunk * ChunkLength, origin.y, 0f);

        int attempt = GetAttempt(slot);
        int baseIdx = slot * TilesPerChunk;
        for (int lz = 0; lz < TileH; lz++)
        {
            for (int lx = 0; lx < TileW; lx++)
            {
                int t = ComputeTileType(worldChunk, seed, attempt, lx, lz);
                int idx = baseIdx + lx + lz * TileW;
                _tileTypes[idx] = t;
                _hp[idx] = t == 0 ? treeHp : (t == 1 ? rockHp : 0);
                _allocated[idx] = false;
                _tileToPool[idx] = -1;
            }
        }
        AllocatePool(slot, worldChunk, seed);
    }

    private void AllocatePool(int slot, int worldChunk, int seed)
    {
        int baseIdx = slot * TilesPerChunk;
        int poolBase = slot * _nodesPerChunk;
        if (_nodesPerChunk <= 0) return;
        int budget = Mathf.Min(poolBudgetPerChunk, _nodesPerChunk);
        int hardCap = Mathf.RoundToInt(budget * hardCapPercent / 100f);
        int used = 0;
        int hardUsed = 0;

        int start = (int)(NoiseHash(worldChunk, seed, 0xB5297A4Du) & 0x7FFFFFFFu) % TilesPerChunk;

        for (int k = 0; k < TilesPerChunk && hardUsed < hardCap && used < budget; k++)
        {
            int t = (start + k * 179) % TilesPerChunk;
            if (_tileTypes[baseIdx + t] != 2) continue;
            if (MaskBit(slot, t)) continue;
            AssignPool(poolBase + used, baseIdx + t, 2, worldChunk, seed);
            used++;
            hardUsed++;
        }
        for (int k = 0; k < TilesPerChunk && used < budget; k++)
        {
            int t = (start + k * 179) % TilesPerChunk;
            if (_tileTypes[baseIdx + t] != 3) continue;
            if (MaskBit(slot, t)) continue;
            AssignPool(poolBase + used, baseIdx + t, 3, worldChunk, seed);
            used++;
        }
        for (int k = 0; k < TilesPerChunk && used < budget; k++)
        {
            int t = (start + k * 179) % TilesPerChunk;
            int type = _tileTypes[baseIdx + t];
            if (type < 0 || type == 3) continue;
            if (_allocated[baseIdx + t]) continue;
            if (MaskBit(slot, t)) continue;
            AssignPool(poolBase + used, baseIdx + t, type, worldChunk, seed);
            used++;
        }
        for (int k = used; k < _nodesPerChunk; k++)
        {
            Transform node = nodeRoots[poolBase + k];
            if (node != null && node.gameObject.activeSelf) node.gameObject.SetActive(false);
        }
    }

    private void AssignPool(int poolIdx, int tIdx, int type, int worldChunk, int seed)
    {
        Transform node = nodeRoots[poolIdx];
        if (node == null) return;
        int local = tIdx % TilesPerChunk;
        int lx = local % TileW;
        int lz = local / TileW;
        int wxTile = worldChunk * TileW + lx;
        int wzTile = TileZMin + lz + Mathf.RoundToInt(origin.z);
        float jx = type == 3 ? 0f : TileJitter(wxTile, wzTile, seed, 0) / 100f;
        float jz = type == 3 ? 0f : TileJitter(wxTile, wzTile, seed, 1) / 100f;
        float px = wxTile + jx;
        float pz = wzTile + jz;
        node.position = new Vector3(origin.x + px, 0f, pz);
        SetPoolVisual(poolIdx, type);
        if (!node.gameObject.activeSelf) node.gameObject.SetActive(true);
        _allocated[tIdx] = true;
        _tileToPool[tIdx] = poolIdx;
    }

    private void SetPoolVisual(int poolIdx, int type)
    {
        Transform t = _visT[poolIdx];
        if (t != null && t.gameObject.activeSelf != (type == 0)) t.gameObject.SetActive(type == 0);
        Transform r = _visR[poolIdx];
        if (r != null && r.gameObject.activeSelf != (type == 1)) r.gameObject.SetActive(type == 1);
        Transform h = _visH[poolIdx];
        if (h != null && h.gameObject.activeSelf != (type == 2)) h.gameObject.SetActive(type == 2);
        Transform p = _visP[poolIdx];
        if (p != null && p.gameObject.activeSelf != (type == 3)) p.gameObject.SetActive(type == 3);
    }

    // ---------- BFS 経路検証 (破壊不能岩のみ通行不可) ----------

    private bool TryGetHeadTile(out int hx, out int hz)
    {
        hx = 0;
        hz = 0;
        if (trackManager == null) return false;
        int p = trackManager.GetHeadTilePacked();
        if (p < 0) return false;
        hx = trackManager.UnpackX(p);
        hz = trackManager.UnpackZ(p);
        return true;
    }

    private void BfsEnqueue(int gx, int gz, ref int qt)
    {
        int idx = gx * BfsH + gz;
        _bfsVisited[idx] = true;
        _bfsQueue[qt] = idx;
        qt++;
    }

    private bool BfsReachGoal(int seed)
    {
        int baseTile = baseChunk * TileW;
        int total = BfsW * BfsH;
        for (int i = 0; i < total; i++)
        {
            _bfsBlocked[i] = false;
            _bfsVisited[i] = false;
        }
        for (int slot = 0; slot < ChunkCount; slot++)
        {
            int wc = WorldChunkForSlot(slot);
            int attempt = GetAttempt(slot);
            for (int lz = 0; lz < TileH; lz++)
            {
                for (int lx = 0; lx < TileW; lx++)
                {
                    if (ComputeTileType(wc, seed, attempt, lx, lz) != 2) continue;
                    int gx = wc * TileW + lx - baseTile;
                    if (gx < 0 || gx >= BfsW) continue;
                    _bfsBlocked[gx * BfsH + lz] = true;
                }
            }
        }

        int qh = 0;
        int qt = 0;
        _bfsMaxX = 0;
        if (TryGetHeadTile(out int hx, out int hz))
        {
            int gx = Mathf.Clamp(hx - baseTile, 0, BfsW - 1);
            int gz = Mathf.Clamp(hz - TileZMin, 0, BfsH - 1);
            BfsEnqueue(gx, gz, ref qt);
        }
        else
        {
            for (int gz = 0; gz < BfsH; gz++)
            {
                if (!_bfsBlocked[gz]) BfsEnqueue(0, gz, ref qt);
            }
        }

        while (qh < qt)
        {
            int cur = _bfsQueue[qh];
            qh++;
            int gx = cur / BfsH;
            int gz = cur % BfsH;
            if (gx > _bfsMaxX) _bfsMaxX = gx;
            if (gx == BfsW - 1) return true;
            for (int d = 0; d < 4; d++)
            {
                int nx = gx + (d == 0 ? 1 : (d == 1 ? -1 : 0));
                int nz = gz + (d == 2 ? 1 : (d == 3 ? -1 : 0));
                if (nx < 0 || nx >= BfsW || nz < 0 || nz >= BfsH) continue;
                int nidx = nx * BfsH + nz;
                if (_bfsBlocked[nidx] || _bfsVisited[nidx]) continue;
                BfsEnqueue(nx, nz, ref qt);
            }
        }
        return false;
    }

    private void ValidateWindow()
    {
        if (gameManager == null) return;
        int seed = gameManager.runSeed;
        if (seed == 0) return;
        for (int iter = 0; iter <= 18; iter++)
        {
            if (BfsReachGoal(seed)) return;
            int wallChunk = baseChunk + (_bfsMaxX + 1) / TileW;
            if (wallChunk >= baseChunk + ChunkCount) break;
            int slot = ((wallChunk % ChunkCount) + ChunkCount) % ChunkCount;
            int att = GetAttempt(slot);
            if (att >= maxAttempt) break;
            SetAttempt(slot, att + 1);
        }
        Debug.LogWarning("[ChunkManager] ValidateWindow: 経路確保できず。attempt上限または範囲外");
    }
}
