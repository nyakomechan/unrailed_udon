#!/usr/bin/env python3
# terrain_cluster_preview.py - 1mタイル・クラスタ方式のプレビュー
# terrain_noise_proto.py の固定小数点Perlinをタイル中心(1m)で評価して
# 森/鉱脈/湖のクラスタ形状・密度・プール割当を可視化する

import sys, os
from collections import deque

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from terrain_noise_proto import noise1000, _hash, WL_TREE, WL_ROCK, WL_WATER, LAYER_TREE, LAYER_ROCK, LAYER_WATER, OFFSET_CM

SEED = 402052
ORIGIN_Z = 6            # 線路中心 (world z)
Z0, Z1 = -4, 15         # 描画z範囲 (world)
X_CHUNKS = 4            # 描画チャンク数 (x 0..95)
POOL_BUDGET = 64        # プール数/チャンク
CORRIDOR_CHUNKS = 2

# タイプ: 0=木 1=岩 2=硬岩 3=池
CHARS = {0: 'T', 1: 'R', 2: 'H', 3: 'P', -1: ' '}
THR = (665, 700, 848, 840)   # (tree, rock, hard, water) tier0目安


def tile_type(wx, wz, thr=THR):
    tT, tR, tH, tW = thr
    wc = wx // 24
    if wc < CORRIDOR_CHUNKS and abs(wz - ORIGIN_Z) < 2:
        return -1
    px = wx * 100 + OFFSET_CM
    pz = wz * 100 + OFFSET_CM
    rv = noise1000(px, pz, WL_ROCK[0], WL_ROCK[1], SEED ^ LAYER_ROCK)
    if noise1000(px, pz, WL_WATER[0], WL_WATER[1], SEED ^ LAYER_WATER) >= tW:
        return 3
    if rv >= tH:
        return 2
    if rv >= tR:
        return 1
    if noise1000(px, pz, WL_TREE[0], WL_TREE[1], SEED ^ LAYER_TREE) >= tT:
        return 0
    return -1


def gen_field(thr=THR):
    field = {}
    for wx in range(X_CHUNKS * 24):
        for wz in range(Z0, Z1 + 1):
            field[(wx, wz)] = tile_type(wx, wz, thr)
    return field


def pool_trim(field):
    # チャンクごと: タイプ付きタイルをハッシュ順で POOL_BUDGET まで割当。
    # 割当=None(見た目なし=通行可), 割当=タイプ
    alloc = {}
    for c in range(X_CHUNKS):
        typed = [(wx, wz) for wx in range(c * 24, (c + 1) * 24) for wz in range(Z0, Z1 + 1)
                 if field[(wx, wz)] >= 0]
        typed.sort(key=lambda p: _hash(p[0], p[1], (SEED ^ 0xA53A9D01) & 0xFFFFFFFF))
        keep = set(typed[:POOL_BUDGET])
        for p in typed:
            alloc[p] = field[p] if p in keep else None
    return alloc


def render(field, alloc=None, title=""):
    print(f"=== {title} ===")
    for wz in range(Z1, Z0 - 1, -1):
        line = ""
        for wx in range(X_CHUNKS * 24):
            t = field[(wx, wz)]
            if alloc is not None and t >= 0:
                a = alloc.get((wx, wz))
                line += CHARS[t] if a is not None else '.'
            else:
                line += CHARS[t] if t >= 0 else ('-' if abs(wz - ORIGIN_Z) < 2 and (wx // 24) < CORRIDOR_CHUNKS else ' ')
        mark = "  < TRACK" if wz == ORIGIN_Z else ""
        print(f"|{line}| z={wz}{mark}")
    cnt = {}
    for v in field.values():
        if v >= 0:
            cnt[CHARS[v]] = cnt.get(CHARS[v], 0) + 1
    total = sum(cnt.values())
    print(f"tiles: {cnt} total={total}/{X_CHUNKS * 24 * (Z1 - Z0 + 1)}")
    if alloc is not None:
        kept = sum(1 for a in alloc.values() if a is not None)
        print(f"pool: kept={kept}/{total} typed tiles (budget {POOL_BUDGET}/chunk)")


def clusters(field, want):
    seen = set()
    sizes = []
    for p, t in field.items():
        if t != want or p in seen:
            continue
        dq = deque([p]); seen.add(p); n = 0
        while dq:
            x, z = dq.popleft(); n += 1
            for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                q = (x + dx, z + dz)
                if field.get(q, -1) == want and q not in seen:
                    seen.add(q); dq.append(q)
        sizes.append(n)
    return sizes


import sys as _s
THR = (665, 700, 848, 840)
POOL_BUDGET = 64
if len(_s.argv) > 1:
    POOL_BUDGET = int(_s.argv[1])
if len(_s.argv) > 2:
    THR = tuple(int(v) for v in _s.argv[2].split(','))

f = gen_field(THR)
render(f, alloc=pool_trim(f), title=f"seed={SEED} thr(T,R,H,W)={THR} pool={POOL_BUDGET}/chunk ('.'=trimmed)")

for name, want in (("tree", 0), ("rock", 1), ("pond", 3)):
    s = sorted(clusters(f, want), reverse=True)
    print(f"{name} clusters: n={len(s)} sizes(top8)={s[:8]}")
