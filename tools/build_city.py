#!/usr/bin/env python3
"""data/raw のOSM JSONを、Unityが読む city.json(ハチ公像を原点、x=東 z=北 [m])に変換する。"""
import json, math
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RAW = ROOT / "data" / "raw"
OUT = ROOT / "game" / "Assets" / "Resources" / "city.json"
LAT0, LON0 = 35.6590597, 139.7006279  # 忠犬ハチ公像
KX = 111320.0 * math.cos(math.radians(LAT0))
KY = 110574.0
WIDTH = {"motorway": 14, "trunk": 14, "primary": 12, "secondary": 10, "tertiary": 8,
         "unclassified": 6, "residential": 6, "living_street": 5, "service": 4,
         "pedestrian": 7, "footway": 3, "steps": 3, "path": 2.5, "cycleway": 3}

def xz(p):
    return round((p["lon"] - LON0) * KX, 2), round((p["lat"] - LAT0) * KY, 2)

def flat(pts):
    out = []
    for p in pts:
        x, z = xz(p); out += [x, z]
    return out

def num(v):
    try: return float(str(v).split()[0].replace("m", ""))
    except Exception: return None

# 高架(橋・デッキ): bridge=yes または layer>=1 の歩行者・車道。両端が他の高架と接していれば高さを保ち、そうでなければ地面へ降りる
LAYER_H = 5.2
def lift_of(t):
    ly = num(t.get("layer")) or 0
    if t.get("bridge") in ("yes", "viaduct", "movable") and ly < 1: ly = 1
    if ly < 1 or t.get("tunnel") == "yes": return 0.0
    return LAYER_H * min(ly, 2)
roads = []
for e in json.load(open(RAW / "roads.json"))["elements"]:
    g = e.get("geometry") or []
    if len(g) < 2: continue
    t = e["tags"]; hw = t["highway"].replace("_link", "")
    if t.get("area") == "yes": continue
    w = num(t.get("width")) or WIDTH.get(hw, 5)
    roads.append({"cls": hw, "name": t.get("name", ""), "w": w, "walk": hw not in ("motorway", "trunk"), "sig": 1 if t.get("crossing") == "traffic_signals" else 0, "lift": lift_of(t) if hw != "steps" else 0.0, "cross": 1 if (t.get("footway") == "crossing" or t.get("highway") == "crossing") else 0,
                  "bridge": t.get("bridge") == "yes" or t.get("layer") in ("1", "2"),
                  "tunnel": t.get("tunnel") == "yes" or (num(t.get("layer")) or 0) < 0,
                  "p": flat(g)})

# PLATEAU(LOD2)の建物高さ。OSM建物の代表点を含むPLATEAU建物の高さを採用する
plateau = []
pf = RAW / "plateau.json"
if pf.exists():
    for o in json.load(open(pf)):
        if o["h"] is None: continue
        pts = [((lo - LON0) * KX, (la - LAT0) * KY) for lo, la in o["p"]]
        xs = [q[0] for q in pts]; zs = [q[1] for q in pts]
        plateau.append((min(xs), min(zs), max(xs), max(zs), pts, o["h"]))
CELL = 30.0
pgrid = {}
for i, b in enumerate(plateau):
    for cx in range(int(b[0] // CELL), int(b[2] // CELL) + 1):
        for cz in range(int(b[1] // CELL), int(b[3] // CELL) + 1):
            pgrid.setdefault((cx, cz), []).append(i)

def inside(pts, x, z):
    c = False; j = len(pts) - 1
    for i in range(len(pts)):
        if (pts[i][1] > z) != (pts[j][1] > z) and x < (pts[j][0] - pts[i][0]) * (z - pts[i][1]) / (pts[j][1] - pts[i][1]) + pts[i][0]:
            c = not c
        j = i
    return c

def plateau_height(flatpts):
    n = len(flatpts) // 2
    cx = sum(flatpts[0::2]) / n; cz = sum(flatpts[1::2]) / n
    probes = [(cx, cz)] + [((cx + flatpts[2 * k]) / 2, (cz + flatpts[2 * k + 1]) / 2) for k in range(n)]
    for x, z in probes:
        hs = [plateau[i][5] for i in pgrid.get((int(x // CELL), int(z // CELL)), []) if
              plateau[i][0] <= x <= plateau[i][2] and plateau[i][1] <= z <= plateau[i][3] and inside(plateau[i][4], x, z)]
        if hs: return max(hs)
    return None

stat = {"plateau": 0, "osm": 0, "default": 0}
ends = {}
for r in roads:
    if r["lift"] > 0:
        for k in (0, len(r["p"]) - 2):
            ends[(r["p"][k], r["p"][k + 1])] = ends.get((r["p"][k], r["p"][k + 1]), 0) + 1
for r in roads:
    n = len(r["p"])
    r["l0"] = r["lift"] if ends.get((r["p"][0], r["p"][1]), 0) >= 2 else 0.0
    r["l1"] = r["lift"] if ends.get((r["p"][n - 2], r["p"][n - 1]), 0) >= 2 else 0.0
print("lifted roads:", sum(1 for r in roads if r["lift"] > 0))

buildings = []
for e in json.load(open(RAW / "buildings.json"))["elements"]:
    rings = []
    if e["type"] == "way":
        rings = [e.get("geometry") or []]
    else:
        rings = [m["geometry"] for m in e.get("members", []) if m.get("role") == "outer" and m.get("geometry")]
    t = e.get("tags", {})
    h_osm = num(t.get("height"))
    if h_osm is None:
        lv = num(t.get("building:levels"))
        h_osm = lv * 3.3 if lv else None
    for r in rings:
        if len(r) < 4: continue
        pts = flat(r[:-1] if r[0] == r[-1] else r)
        hp = plateau_height(pts) if plateau else None
        if hp is not None: h, src = hp, "plateau"
        elif h_osm is not None: h, src = h_osm, "osm"
        else: h, src = 14.0, "default"
        stat[src] += 1
        buildings.append({"h": round(max(4.0, min(h, 260.0)), 1), "src": src, "name": t.get("name", ""), "p": pts})

pois = []
want = {
    136691387: ("メガドンキ 渋谷本店", "way"),
    5296991943: ("SHIBUYA TSUTAYA", "node"),
    12452359640: ("TSUTAYA 東口側", "node"),
    617560918: ("渋谷スクランブルスクエア", "way"),
    204091879: ("渋谷ヒカリエ", "way"),
    7544706: ("渋谷マークシティ", "relation"),
}
extra = {136691387: (35.6604804, 139.6978596)}  # 別クエリで取得した重心
for e in json.load(open(RAW / "poi.json"))["elements"]:
    if e["id"] in want:
        c = e.get("center") or e
        pois.append({"id": e["id"], "name": want[e["id"]][0], "x": xz(c)[0], "z": xz(c)[1]})
if not any(p["id"] == 136691387 for p in pois):
    la, lo = extra[136691387]
    pois.append({"id": 136691387, "name": want[136691387][0], "x": xz({"lat": la, "lon": lo})[0], "z": xz({"lat": la, "lon": lo})[1]})

def points(name):
    f = RAW / f"{name}.json"
    if not f.exists(): return []
    out = []
    for e in json.load(open(f))["elements"]:
        if e["type"] == "node":
            x, z = xz(e); out += [x, z]
    return out
trees = points("trees"); lamps = points("lamps")
print("trees", len(trees) // 2, "lamps", len(lamps) // 2)

OUT.parent.mkdir(parents=True, exist_ok=True)
OUT.write_text(json.dumps({"roads": roads, "buildings": buildings, "pois": pois, "trees": trees, "lamps": lamps},
                          ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
print("building height source:", stat)
print(f"roads={len(roads)} buildings={len(buildings)} pois={len(pois)} {OUT.stat().st_size/1e6:.1f}MB")
for p in pois: print(p)
