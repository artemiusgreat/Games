#!/usr/bin/env python3
"""Fetch the CC0 photoreal probe assets from Poly Haven.

Textures (ground/wall sets): 2K JPG Diffuse + nor_gl + arm maps.
Models: raw GLTF at 1K into models_raw/ — Poly Haven's trees and
rocks are full photogrammetry scans (tens of MB of geometry), so
decimate_models.py (Blender) turns them into game-weight assets
under models/ afterwards. Grass ships small and is used as-is.
Poly Haven content is CC0: https://polyhaven.com/license
"""
import json
import os
import sys
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
API = "https://api.polyhaven.com/files/{}"
UA = {"User-Agent": "Mozilla/5.0"}

TEXTURES = ["leafy_grass", "forest_floor", "aerial_rocks_02",
            "aerial_asphalt_01", "concrete_block_wall_02"]
TEX_MAPS = ("Diffuse", "nor_gl", "arm")
MODELS = ["island_tree_01", "island_tree_02", "island_tree_03",
          "coast_rocks_01", "grass_medium_01"]


def get_json(url):
    return json.load(urllib.request.urlopen(
        urllib.request.Request(url, headers=UA), timeout=60))


def fetch(url, dest):
    if os.path.exists(dest) and os.path.getsize(dest) > 0:
        return
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req, timeout=600) as r, open(dest, "wb") as f:
        while chunk := r.read(1 << 20):
            f.write(chunk)
    print("  got", os.path.relpath(dest, HERE),
          f"{os.path.getsize(dest) // 1024} KB")


def main():
    for tid in TEXTURES:
        print("texture", tid)
        data = get_json(API.format(tid))
        dest = os.path.join(HERE, "textures", tid)
        for m in TEX_MAPS:
            entry = data.get(m, {}).get("2k", {}).get("jpg")
            if entry:
                fetch(entry["url"], os.path.join(
                    dest, entry["url"].split("/")[-1]))
    for mid in MODELS:
        print("model", mid)
        data = get_json(API.format(mid))
        g = data["gltf"]["1k"]["gltf"]
        dest = os.path.join(HERE, "models_raw", mid)
        fetch(g["url"], os.path.join(dest, g["url"].split("/")[-1]))
        for rel, info in g.get("include", {}).items():
            fetch(info["url"], os.path.join(dest, rel))
    print("done")


if __name__ == "__main__":
    sys.exit(main())
