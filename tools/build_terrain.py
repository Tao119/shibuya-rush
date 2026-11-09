#!/usr/bin/env python3
"""国土地理院のDEM(dem5a_png, z15)から標高格子を作り、game/Assets/Resources/terrain.bytes に出力する。
座標はbuild_city.pyと同じ(ハチ公像が原点、x=東 z=北 [m])。標高はハチ公像を0にした相対値。
形式: float32 minX, minZ, step, nx, nz のあと nz*nx 個のfloat32(行=z, 列=x)。出典: 国土地理院。"""
import io, math, struct, urllib.request
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
RAW = ROOT / "data" / "raw" / "dem"; RAW.mkdir(parents=True, exist_ok=True)
LAT0, LON0 = 35.6590597, 139.7006279
KX = 111320.0 * math.cos(math.radians(LAT0)); KY = 110574.0
MINX, MAXX, MINZ, MAXZ, STEP = -800.0, 760.0, -780.0, 940.0, 4.0
Z = 15

def tile_xy(lat, lon):
    n = 2 ** Z
    fx = (lon + 180) / 360 * n
    fy = (1 - math.log(math.tan(math.radians(lat)) + 1 / math.cos(math.radians(lat))) / math.pi) / 2 * n
    return fx, fy

tiles = {}
def tile(tx, ty):
    if (tx, ty) not in tiles:
        f = RAW / f"{Z}_{tx}_{ty}.png"
        if not f.exists():
            u = f"https://cyberjapandata.gsi.go.jp/xyz/dem5a_png/{Z}/{tx}/{ty}.png"
            try:
                f.write_bytes(urllib.request.urlopen(urllib.request.Request(u, headers={"User-Agent": "shibuya-rush-dev/0.1"}), timeout=60).read())
            except Exception:
                u = f"https://cyberjapandata.gsi.go.jp/xyz/dem_png/14/{tx//2}/{ty//2}.png"  # 5mが無ければ10m
                raise SystemExit(f"DEM5Aタイルが取得できません: {tx},{ty}")
        a = np.asarray(Image.open(f).convert("RGB")).astype(np.int64)
        v = a[..., 0] * 65536 + a[..., 1] * 256 + a[..., 2]
        h = np.where(v == 2 ** 23, np.nan, np.where(v < 2 ** 23, v, v - 2 ** 24) * 0.01)
        tiles[(tx, ty)] = h
    return tiles[(tx, ty)]

def elev(lat, lon):
    fx, fy = tile_xy(lat, lon); tx, ty = int(fx), int(fy)
    h = tile(tx, ty); px = (fx - tx) * 256 - 0.5; py = (fy - ty) * 256 - 0.5
    x0, y0 = int(math.floor(px)), int(math.floor(py)); ax, ay = px - x0, py - y0
    def g(x, y):
        t = (tx, ty)
        if x < 0 or x > 255 or y < 0 or y > 255:
            return tile(tx + (x // 256), ty + (y // 256))[y % 256, x % 256]
        return h[y, x]
    v = [g(x0, y0), g(x0 + 1, y0), g(x0, y0 + 1), g(x0 + 1, y0 + 1)]
    if any(np.isnan(v)): return float("nan")
    return (v[0] * (1 - ax) + v[1] * ax) * (1 - ay) + (v[2] * (1 - ax) + v[3] * ax) * ay

nx = int((MAXX - MINX) / STEP) + 1; nz = int((MAXZ - MINZ) / STEP) + 1
grid = np.zeros((nz, nx), dtype=np.float32)
for j in range(nz):
    lat = LAT0 + (MINZ + j * STEP) / KY
    for i in range(nx):
        lon = LON0 + (MINX + i * STEP) / KX
        grid[j, i] = elev(lat, lon)
nans = int(np.isnan(grid).sum())
if nans:
    m = np.nanmean(grid); grid = np.where(np.isnan(grid), m, grid)
base = elev(LAT0, LON0)
grid -= np.float32(base)
out = ROOT / "game" / "Assets" / "Resources" / "terrain.bytes"
out.write_bytes(struct.pack("<fffii", MINX, MINZ, STEP, nx, nz) + grid.astype("<f4").tobytes())
print(f"grid {nx}x{nz} step={STEP}m  nan={nans}  hachiko={base:.1f}m  min={grid.min():.1f} max={grid.max():.1f}  -> {out.stat().st_size/1e3:.0f}KB")
