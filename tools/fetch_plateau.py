#!/usr/bin/env python3
"""PLATEAU(LOD2のMVT, indigo-lab配信)から渋谷周辺の建物高さを取得し、data/raw/plateau.json に保存する。
出典: 国土交通省 Project PLATEAU のデータを加工(indigo-lab/plateau-lod2-mvt)。CC BY 4.0。"""
import json, math, sys, urllib.request
from pathlib import Path
try:
    import mapbox_vector_tile as mvt
except ImportError:
    sys.exit("pip install mapbox-vector-tile が必要です")

RAW = Path(__file__).resolve().parent.parent / "data" / "raw"; RAW.mkdir(parents=True, exist_ok=True)
S, W, N, E = 35.6525, 139.6925, 35.6670, 139.7085
Z = 16

def tile(lat, lon):
    n = 2 ** Z
    return int((lon + 180) / 360 * n), int((1 - math.log(math.tan(math.radians(lat)) + 1 / math.cos(math.radians(lat))) / math.pi) / 2 * n)

def to_lonlat(x, y, tx, ty, ext):
    n = 2 ** Z
    lon = (tx + x / ext) / n * 360 - 180
    lat = math.degrees(math.atan(math.sinh(math.pi * (1 - 2 * (ty + (1 - y / ext)) / n))))
    return lon, lat

x0, y1 = tile(S, W); x1, y0 = tile(N, E)
out = []
for tx in range(x0, x1 + 1):
    for ty in range(y0, y1 + 1):
        u = f"https://indigo-lab.github.io/plateau-lod2-mvt/{Z}/{tx}/{ty}.pbf"
        try:
            data = urllib.request.urlopen(urllib.request.Request(u, headers={"User-Agent": "shibuya-rush-dev/0.1"}), timeout=60).read()
        except Exception as e:
            print("skip", tx, ty, e); continue
        d = mvt.decode(data)
        for lname, layer in d.items():
            ext = layer.get("extent", 4096)
            for f in layer["features"]:
                g = f["geometry"]; props = f["properties"]
                polys = g["coordinates"] if g["type"] == "MultiPolygon" else [g["coordinates"]] if g["type"] == "Polygon" else []
                for p in polys:
                    ring = p[0]
                    out.append({"h": props.get("measuredHeight", props.get("z")), "p": [to_lonlat(a, b, tx, ty, ext) for a, b in ring]})
        print("tile", tx, ty, "total", len(out), "layers", list(d))
(RAW / "plateau.json").write_text(json.dumps(out), encoding="utf-8")
hs = [o["h"] for o in out if o["h"] is not None]
print("buildings", len(out), "with height", len(hs), "max", max(hs) if hs else None)
