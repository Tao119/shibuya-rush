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

roads = []
for e in json.load(open(RAW / "roads.json"))["elements"]:
    g = e.get("geometry") or []
    if len(g) < 2: continue
    t = e["tags"]; hw = t["highway"].replace("_link", "")
    if t.get("area") == "yes": continue
    w = num(t.get("width")) or WIDTH.get(hw, 5)
    roads.append({"cls": hw, "name": t.get("name", ""), "w": w, "walk": hw not in ("motorway", "trunk"),
                  "bridge": t.get("bridge") == "yes" or t.get("layer") in ("1", "2"),
                  "tunnel": t.get("tunnel") == "yes" or (num(t.get("layer")) or 0) < 0,
                  "p": flat(g)})

buildings = []
for e in json.load(open(RAW / "buildings.json"))["elements"]:
    rings = []
    if e["type"] == "way":
        rings = [e.get("geometry") or []]
    else:
        rings = [m["geometry"] for m in e.get("members", []) if m.get("role") == "outer" and m.get("geometry")]
    t = e.get("tags", {})
    h = num(t.get("height"))
    if h is None:
        lv = num(t.get("building:levels"))
        h = lv * 3.3 if lv else 14.0
    h = max(4.0, min(h, 260.0))
    for r in rings:
        if len(r) < 4: continue
        pts = flat(r[:-1] if r[0] == r[-1] else r)
        buildings.append({"h": h, "name": t.get("name", ""), "p": pts})

pois = []
want = {
    136691387: ("メガドンキ(MEGAドン・キホーテ渋谷本店)", "way"),
    5296991943: ("SHIBUYA TSUTAYA", "node"),
    12452359640: ("TSUTAYA(東口側)", "node"),
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

OUT.parent.mkdir(parents=True, exist_ok=True)
OUT.write_text(json.dumps({"roads": roads, "buildings": buildings, "pois": pois},
                          ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
print(f"roads={len(roads)} buildings={len(buildings)} pois={len(pois)} {OUT.stat().st_size/1e6:.1f}MB")
for p in pois: print(p)
