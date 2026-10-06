#!/usr/bin/env python3
# terrain_noise_proto.py - v2 地形生成プロトタイプ
# 位置: 現行v1互換 (セル+ジッター, .NET System.Random 再現, 3回消費維持)
# タイプ: 固定小数点Perlinノイズ (整数演算のみ, C#/UdonSharp へそのまま移植可能な設計)
# v2.1: コリドーは先頭 CorridorChunks チャンクのみ。破壊不能岩は attempt ラダーで段階的に
#       密度を下げられる (BFS検証で経路不通の場合にオーナーが attempt を上げる運用)。
#       attempt は tH しきい値だけを変えるので、他タイプと採掘ビットマップに影響しない。

from collections import deque

MBIG = 2147483647

# ---------- .NET System.Random 再現 (Unity mono 互換) ----------
# 混合seedが負の場合に初期化 `mk = mj - mk` 等が int32 オーバーフローで wrap する。
# .NET は unchecked で黙って wrap するので、全算術に wrap32 を入れて再現する
# (wrapしないと SeedArray の1要素だけがズレ、散発的に1回抽選がズレる症状が出る)。
def _w32(v):
    v &= 0xFFFFFFFF
    return v - 0x100000000 if v >= 0x80000000 else v


class DotNetRandom:
    def __init__(self, seed):
        MSEED = 161803398
        subtraction = MBIG if seed == -2147483648 else abs(seed)
        mj = _w32(MSEED - subtraction)
        self.sa = [0] * 56
        self.sa[55] = mj
        mk = 1
        for i in range(1, 55):
            ii = (21 * i) % 55
            self.sa[ii] = mk
            mk = _w32(mj - mk)
            if mk < 0:
                mk += MBIG
            mj = self.sa[ii]
        for _ in range(4):
            for i in range(1, 56):
                self.sa[i] = _w32(self.sa[i] - self.sa[1 + (i + 30) % 55])
                if self.sa[i] < 0:
                    self.sa[i] += MBIG
        self.inext = 0
        self.inextp = 21

    def _sample(self):
        self.inext += 1
        if self.inext >= 56:
            self.inext = 1
        self.inextp += 1
        if self.inextp >= 56:
            self.inextp = 1
        ret = _w32(self.sa[self.inext] - self.sa[self.inextp])
        if ret == MBIG:
            ret -= 1
        if ret < 0:
            ret += MBIG
        self.sa[self.inext] = ret
        return ret

    def next(self, maxval):
        return int(self._sample() * (1.0 / MBIG) * maxval)


def mix_seed(seed, chunk):
    v = (seed * 73856093 + chunk * 19349663 + 12345) & 0xFFFFFFFF
    return v - 0x100000000 if v >= 0x80000000 else v


# ---------- 固定小数点 Perlin ノイズ (Q10, 整数のみ) ----------
Q = 1024
GRAD8 = ((1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1))
NORM = 724  # 2D Perlin (8勾配) の理論最大 |n| (Q10)


def _hash(ix, iz, s):
    h = ((ix & 0xFFFFFFFF) * 374761393
         + (iz & 0xFFFFFFFF) * 668265263
         + (s & 0xFFFFFFFF) * 974711) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return h ^ (h >> 16)


def _fade(t):
    t2 = (t * t) >> 10
    t3 = (t2 * t) >> 10
    return (t3 * (6 * t2 - 15 * t + 10 * Q)) >> 10


def noise_q10(px, pz, wlx, wlz, s):
    # px, pz: 非負の整数cm, wlx/wlz: 波長cm (異方性), 戻り値: 生値 Q10 (おおよそ ±724)
    gx = (px * Q) // wlx
    gz = (pz * Q) // wlz
    ix, fx = gx >> 10, gx & 1023
    iz, fz = gz >> 10, gz & 1023
    g = GRAD8[_hash(ix, iz, s) & 7]
    d00 = g[0] * fx + g[1] * fz
    g = GRAD8[_hash(ix + 1, iz, s) & 7]
    d10 = g[0] * (fx - Q) + g[1] * fz
    g = GRAD8[_hash(ix, iz + 1, s) & 7]
    d01 = g[0] * fx + g[1] * (fz - Q)
    g = GRAD8[_hash(ix + 1, iz + 1, s) & 7]
    d11 = g[0] * (fx - Q) + g[1] * (fz - Q)
    u = _fade(fx)
    v = _fade(fz)
    nx0 = d00 + ((u * (d10 - d00)) >> 10)
    nx1 = d01 + ((u * (d11 - d01)) >> 10)
    return nx0 + ((v * (nx1 - nx0)) >> 10)


def noise1000(px, pz, wlx, wlz, s):
    v = ((noise_q10(px, pz, wlx, wlz, s) + NORM) * 1000) // (2 * NORM)
    return 0 if v < 0 else (1000 if v > 1000 else v)


# ---------- レイヤ定義 ----------
# 波長cm (x, z)。チャンク幅2400cmの整数倍を避ける。z全長は約20m。
# 水は xに長く zに狭く = 線路に沿った湖。zを全幅塞ぐ帯状湖を防止
WL_TREE = (1600, 1200)
WL_ROCK = (1300, 1100)
WL_WATER = (4000, 1300)
LAYER_TREE, LAYER_ROCK, LAYER_WATER = 0x9E3779B9, 0x3C6EF372, 0x7F4A7C15
LAYER_WOBBLE = 0xB5297A4D     # zワブル変調用 (乱数消費ゼロで決定的)
LAYER_FLATZ = 0xD1B54A35      # z全展開配置 (flat方式) 用
OFFSET_CM = 10000  # 負座標回避の +100m オフセット

# 配置方式: "wobble" = v1ジッター + ±150cm ワブル / "flat" = zをピッチ500cmで全展開 (範囲はv1と同一・完全均一)
POSITION_MODE = "flat"
FLAT_Z_PITCH = 500

CORRIDOR_CHUNKS = 2           # コリドーを適用する先頭チャンク数 (スターター線路30枚をカバー)
ATTEMPT_SCALE = (1.0, 0.5, 0.25, 0.0)  # attempt ごとの破壊不能岩率スケール
MAX_ATTEMPT = 3
STATION_TILES = 40            # 駅間隔 (仮, M4で本決め)
WOBBLE_AMP = 150              # zワブル振幅cm。ギャップ(約3m)の半分。これ未満だと帯の中心が窪む

# BFS検証グリッド (docフレーム): ワブル後ノードタイルz ∈ [-6,16] をカバー
ZMIN, ZW = -7, 24
WINDOW = 6                    # アクティブウィンドウ (チャンク数)

# タイプ: -1=空 0=木 1=岩 2=破壊不能岩 3=池
CHARS = {-1: ' ', 0: 'T', 1: 'R', 2: 'H', 3: 'P'}


def slot_wobble_cm(worldChunk, s, seed):
    # スロットごとのzワブル。乱数消費ゼロ・ハッシュ派生で決定的 (v1位置互換のrng列に影響しない)
    return (_hash(worldChunk, s, (seed ^ LAYER_WOBBLE) & 0xFFFFFFFF) % (2 * WOBBLE_AMP)) - WOBBLE_AMP


def slot_positions(worldChunk, seed, mode=POSITION_MODE):
    # v1互換: 1スロット3回消費 (3回目はv1位置維持のため消費のみ)。消費は方式に関わらず実施
    # mode: "off"=変調無し(v1) / "wobble"=v1+±150cm / "flat"=zをピッチ500cmでハッシュ全展開
    rng = DotNetRandom(mix_seed(seed, worldChunk))
    out = []
    for s in range(24):
        cellX, cellZ = s % 6, s // 6
        lx_cm = cellX * 400 + 80 + rng.next(240)
        lz_cm = -450 + cellZ * 575 + rng.next(275)
        rng.next(1000)
        if mode == "flat":
            lz_cm = -450 + cellZ * FLAT_Z_PITCH + _hash(worldChunk, s, (seed ^ LAYER_FLATZ) & 0xFFFFFFFF) % FLAT_Z_PITCH
        elif mode == "wobble":
            lz_cm += slot_wobble_cm(worldChunk, s, seed)
        out.append((lx_cm, lz_cm))
    return out


def gen_chunk(worldChunk, seed, thr, mode=POSITION_MODE):
    # thr = (tW, tH, tR, tT) 各ノイズ 0..1000 のしきい値
    tW, tH, tR, tT = thr
    nodes = []
    for lx_cm, lz_cm in slot_positions(worldChunk, seed, mode):
        t = -1
        in_corridor = (worldChunk < CORRIDOR_CHUNKS
                       and abs(lz_cm - 600) < 200)
        if not in_corridor:
            px = worldChunk * 2400 + lx_cm + OFFSET_CM
            pz = lz_cm + OFFSET_CM
            rock_v = noise1000(px, pz, WL_ROCK[0], WL_ROCK[1], seed ^ LAYER_ROCK)
            if noise1000(px, pz, WL_WATER[0], WL_WATER[1], seed ^ LAYER_WATER) >= tW:
                t = 3
            elif rock_v >= tH:
                t = 2
            elif rock_v >= tR:
                t = 1
            elif noise1000(px, pz, WL_TREE[0], WL_TREE[1], seed ^ LAYER_TREE) >= tT:
                t = 0
        nodes.append((lx_cm, lz_cm, t))
    return nodes


def build_thresholds(seed, chunks=400):
    # ノード位置での各レイヤの分布を実測し、率(%)→しきい値 の変換表を作る
    layers = [(WL_TREE, LAYER_TREE), (WL_ROCK, LAYER_ROCK), (WL_WATER, LAYER_WATER)]
    tables = []
    for (wlx, wlz), layer in layers:
        vals = []
        for wc in range(chunks):
            for lx_cm, lz_cm in slot_positions(wc, seed):
                vals.append(noise1000(wc * 2400 + lx_cm + OFFSET_CM,
                                      lz_cm + OFFSET_CM, wlx, wlz, seed ^ layer))
        vals.sort()
        tables.append(vals)
    return tables


def thr_for_rate(sorted_vals, rate_pct):
    # 上位 rate_pct % に入るためのしきい値。率0は不可能値
    if rate_pct <= 0.0:
        return 1001
    idx = int(len(sorted_vals) * (100.0 - rate_pct) / 100.0)
    return sorted_vals[min(len(sorted_vals) - 1, max(0, idx))]


def tier_of(worldChunk, station_tiles=STATION_TILES):
    return worldChunk * 24 // station_tiles


def base_rates(tier):
    rate_water = min(12.0, 4.0 + 2.0 * tier)
    rate_hard = min(10.0, 3.0 + 1.5 * tier)
    rate_rock = 10.0
    rate_tree = min(36.0, 30.0 + 0.75 * tier)
    return rate_water, rate_hard, rate_rock, rate_tree


def thresholds_for_tier(tables, tier, attempt=0):
    tree_t, rock_t, water_t = tables
    rate_water, rate_hard, rate_rock, rate_tree = base_rates(tier)
    hard = rate_hard * ATTEMPT_SCALE[attempt]
    return (thr_for_rate(water_t, rate_water),
            thr_for_rate(rock_t, hard),
            thr_for_rate(rock_t, hard + rate_rock),
            thr_for_rate(tree_t, rate_tree))


# ---------- BFS 検証シミュレータ ----------
def window_blocked(wc0, seed, tables, tier, attempts):
    # ウィンドウ [wc0, wc0+6) の破壊不能岩タイル集合を (gx, gz) で返す
    blocked = set()
    for i in range(WINDOW):
        wc = wc0 + i
        thr = thresholds_for_tier(tables, tier, attempts[i])
        for lx_cm, lz_cm, t in gen_chunk(wc, seed, thr):
            if t == 2:
                blocked.add((i * 24 + lx_cm // 100, lz_cm // 100))
    return blocked


def bfs(blocked):
    # 左端列の通行可能タイル → 右端列。戻り値 (goal到達?, 到達最大x)
    w = WINDOW * 24
    visited = set()
    dq = deque()
    for z in range(ZMIN, ZMIN + ZW):
        if (0, z) not in blocked:
            visited.add((0, z))
            dq.append((0, z))
    maxx = 0
    goal = False
    while dq:
        x, z = dq.popleft()
        if x > maxx:
            maxx = x
        if x == w - 1:
            goal = True
        for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, nz = x + dx, z + dz
            if (0 <= nx < w and ZMIN <= nz < ZMIN + ZW
                    and (nx, nz) not in blocked and (nx, nz) not in visited):
                visited.add((nx, nz))
                dq.append((nx, nz))
    return goal, maxx


def validate_window(wc0, seed, tables, tier):
    # attemptラダーでリトライ。戻り値 (bumps, attempts) 失敗(保険発動)なら None
    attempts = [0] * WINDOW
    bumps = 0
    while True:
        blocked = window_blocked(wc0, seed, tables, tier, attempts)
        goal, maxx = bfs(blocked)
        if goal:
            return bumps, attempts
        i = (maxx + 1) // 24
        if i >= WINDOW or attempts[i] >= MAX_ATTEMPT or bumps >= 18:
            return None
        attempts[i] += 1
        bumps += 1


def validation_sim(seed, tables, tiers=(0, 1, 2, 4, 8), n_windows=200):
    print("--- BFS検証シミュレーション (破壊不能岩のみ通行不可) ---")
    print(f"seed={seed}  ウィンドウ {n_windows} 件/tier, 起点=ウィンドウ左端列")
    for tier in tiers:
        n_bumped = 0
        total_bumps = 0
        max_att = 0
        fails = 0
        for wc0 in range(n_windows):
            res = validate_window(wc0, seed, tables, tier)
            if res is None:
                fails += 1
                continue
            bumps, attempts = res
            if bumps > 0:
                n_bumped += 1
            total_bumps += bumps
            max_att = max(max_att, max(attempts))
        print(f"tier{tier}: リトライ発生 {n_bumped/n_windows*100:4.1f}% "
              f"(avg bumps {total_bumps/n_windows:.3f}/window, max attempt {max_att}, "
              f"保険発動 {fails})")


# ---------- 可視化・統計 ----------
def render(worldChunks, seed, thr, label):
    print(f"--- {label} ---")
    print("    " + "".join(f"| c{wc:<3}" for wc in worldChunks))
    grid = {}
    for ci, wc in enumerate(worldChunks):
        for lx_cm, lz_cm, t in gen_chunk(wc, seed, thr):
            col = ci * 24 + lx_cm // 100
            row = lz_cm // 100
            grid[(row, col)] = t
    for row in range(15, -6, -1):
        line = []
        for ci, wc in enumerate(worldChunks):
            for col_in in range(24):
                col = ci * 24 + col_in
                t = grid.get((row, col), -1)
                if t == -1 and wc < CORRIDOR_CHUNKS and abs(row - 6) < 2:
                    line.append('-')
                else:
                    line.append(CHARS[t])
        print(f"z={row:>3} " + "".join(line))
    counts = []
    for wc in worldChunks:
        c = {0: 0, 1: 0, 2: 0, 3: 0, -1: 0}
        for _, _, t in gen_chunk(wc, seed, thr):
            c[t] += 1
        counts.append(f"T{c[0]} R{c[1]} H{c[2]} P{c[3]} 空{c[-1]}")
    print("     " + "  ".join(counts))
    print("     T=木 R=岩 H=破壊不能岩 P=池 -=コリドー(chunk0-1のみ)")


def stats(seed, tables, tiers=(0, 1, 2, 4, 8), n_chunks=150, wc0=100):
    print("--- 実測密度 (tier固定, wc100以降=コリドー無し, 全24セルが候補) ---")
    print(f"seed={seed}  (名目: 木30+0.75/t cap36, 岩10, H3+1.5/t cap10, 池4+2/t cap12)")
    for tier in tiers:
        thr = thresholds_for_tier(tables, tier)
        tot = {0: 0, 1: 0, 2: 0, 3: 0, -1: 0}
        n_cells = 0
        for wc in range(wc0, wc0 + n_chunks):
            for lx_cm, lz_cm, t in gen_chunk(wc, seed, thr):
                n_cells += 1
                tot[t] += 1
        obs = n_cells - tot[-1]
        print(f"tier{tier:>2}: T{tot[0]/n_cells*100:4.1f}% R{tot[1]/n_cells*100:4.1f}% "
              f"H{tot[2]/n_cells*100:4.1f}% P{tot[3]/n_cells*100:4.1f}% "
              f"障害物計{obs/n_cells*100:4.1f}% "
              f"(avg {obs/n_chunks:4.1f}個/chunk)")


def z_profile(seed, tables, tier, n_chunks=300, wc0=100):
    # タイル行ごとのノード密度プロファイル (配置方式ごと比較)
    print(f"--- z方向ノード密度プロファイル (tier{tier}, {n_chunks}チャンク, wc{wc0}以降) ---")
    print(f"行 | 変調無し | wobble±150 | flat(POSITION_MODE={POSITION_MODE}) : 配置個/chunk と障害物バー")
    results = {}
    for mode in ("off", "wobble", "flat"):
        placed = {}
        obstacles = {}
        thr = thresholds_for_tier(tables, tier)
        for wc in range(wc0, wc0 + n_chunks):
            for lx_cm, lz_cm, t in gen_chunk(wc, seed, thr, mode):
                row = lz_cm // 100
                placed[row] = placed.get(row, 0) + 1
                if t >= 0:
                    obstacles[row] = obstacles.get(row, 0) + 1
        results[mode] = (placed, obstacles)
    for row in range(ZMIN, ZMIN + ZW):
        line = [f"z={row:>3}"]
        for mode in ("off", "wobble", "flat"):
            placed, obstacles = results[mode]
            p = placed.get(row, 0) / n_chunks
            o = obstacles.get(row, 0) / n_chunks
            bar = '#' * int(o * 8)
            line.append(f"{p:4.2f}/{o:4.2f} {bar}")
        print(" | ".join(line))


def tile_collision_check(seed, n_chunks=500, wc0=100):
    # 配置方式ごとに、同じタイルに2ノードが乗る率を計測
    print(f"--- タイル衝突チェック ({n_chunks}チャンク) ---")
    for mode in ("off", "wobble", "flat"):
        n_pairs = 0
        for wc in range(wc0, wc0 + n_chunks):
            seen = {}
            for lx_cm, lz_cm in slot_positions(wc, seed, mode):
                key = (lx_cm // 100, lz_cm // 100)
                if key in seen:
                    n_pairs += 1
                else:
                    seen[key] = True
        print(f"{mode:6s}: 衝突 {n_pairs} ペア ({n_pairs/n_chunks:.4f} ペア/chunk)")


def print_csharp_tables(tables):
    print("--- C# 埋め込み用しきい値テーブル (tier 0..8) ---")
    tW = [thresholds_for_tier(tables, t)[0] for t in range(9)]
    tT = [thresholds_for_tier(tables, t)[3] for t in range(9)]
    tR = [thresholds_for_tier(tables, t)[2] for t in range(9)]
    tH = []
    for t in range(9):
        for a in range(4):
            tH.append(thresholds_for_tier(tables, t, a)[1])
    print(f"ThrWater = {{ {', '.join(map(str, tW))} }};")
    print(f"ThrRock  = {{ {', '.join(map(str, tR))} }};")
    print(f"ThrTree  = {{ {', '.join(map(str, tT))} }};")
    print(f"ThrHard  = {{ {', '.join(map(str, tH))} }};  // [tier*4 + attempt]")


def main():
    # v1検証値: seed=402052, chunk0, Node0 -> (1.86, -2.79) [Unity実測と一致済み]
    # ワブルはv1に無い追加要素なので wobble=False で検証する
    pos = slot_positions(0, 402052, mode="off")
    assert pos[0] == (186, -279), f"v1位置検証NG: {pos[0]}"
    wob = slot_wobble_cm(0, 0, 402052)
    print(f"v1位置検証OK: Node0 = ({pos[0][0]/100}, {pos[0][1]/100}), ワブル {wob}cm → 現行 z = {(-279 + wob)/100}")

    seed = 402052
    tables = build_thresholds(seed)
    names = ["tree", "rock", "water"]
    print("\n--- ノイズ分布 (0..1000 正規化後のパーセンタイル) ---")
    pcts = [10, 25, 50, 60, 70, 75, 80, 85, 88, 90, 92, 94, 96, 98, 99]
    for name, vals in zip(names, tables):
        row = " ".join(f"P{p}={vals[int(len(vals)*p/100)]}" for p in pcts)
        print(f"{name:5s} min={vals[0]} max={vals[-1]}  {row}")

    for tier in (0, 4):
        thr = thresholds_for_tier(tables, tier)
        print(f"\ntier{tier} しきい値: water={thr[0]} hard={thr[1]} rock={thr[2]} tree={thr[3]}")
        wc0 = 0 if tier == 0 else (tier * 40 + 23) // 24
        render(range(wc0, wc0 + 6), seed, thr,
               f"seed={seed} tier{tier} chunks {wc0}..{wc0+5}")

    print()
    stats(seed, tables)
    print()
    z_profile(seed, tables, 2)
    print()
    tile_collision_check(seed)
    print()
    validation_sim(seed, tables)
    print()
    print_csharp_tables(tables)


if __name__ == "__main__":
    main()
