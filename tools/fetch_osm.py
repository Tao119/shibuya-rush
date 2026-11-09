#!/usr/bin/env python3
"""渋谷駅周辺のOSM(道路・建物・目的地候補)を取得して data/raw に保存する。"""
import json, sys, time, urllib.parse, urllib.request
from pathlib import Path

RAW = Path(__file__).resolve().parent.parent / "data" / "raw"
RAW.mkdir(parents=True, exist_ok=True)
BBOX = "35.6525,139.6925,35.6670,139.7085"  # S,W,N,E
ENDPOINTS = ["https://z.overpass-api.de/api/interpreter", "https://lz4.overpass-api.de/api/interpreter"]

QUERIES = {
    "roads": f'[out:json][timeout:120];way["highway"~"^(motorway|trunk|primary|secondary|tertiary|unclassified|residential|living_street|service|pedestrian|footway|steps|path|cycleway|primary_link|secondary_link|tertiary_link|trunk_link)$"]({BBOX});out tags geom;',
    "buildings": f'[out:json][timeout:180];(way["building"]({BBOX});relation["building"]({BBOX}););out tags geom;',
    "poi": f'[out:json][timeout:60];nwr["name"~"ドン・キホーテ|ドンキ|TSUTAYA|ツタヤ|ハチ公|SHIBUYA109|ヒカリエ|スクランブルスクエア|マークシティ|センター街"]({BBOX});out tags center;',
}

def post(q):
    last = None
    for attempt in range(6):
        for ep in ENDPOINTS:
            try:
                req = urllib.request.Request(ep, data=urllib.parse.urlencode({"data": q}).encode(),
                                             headers={"User-Agent": "shibuya-rush-dev/0.1"})
                with urllib.request.urlopen(req, timeout=240) as r:
                    return json.loads(r.read())
            except Exception as e:  # noqa
                last = e
                print("retry", ep, e, file=sys.stderr)
                time.sleep(5 * (attempt + 1))
    raise SystemExit(f"failed: {last}")

for name, q in QUERIES.items():
    out = RAW / f"{name}.json"
    if out.exists() and "--refresh" not in sys.argv:
        print("skip", name); continue
    d = post(q)
    out.write_text(json.dumps(d, ensure_ascii=False), encoding="utf-8")
    print(name, len(d["elements"]), f"{out.stat().st_size/1e6:.1f}MB")
