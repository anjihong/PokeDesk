#!/usr/bin/env python3
"""Generate bounded display-size reference data from the pinned PokeAPI pokemon.csv.

python scripts/generate-pokemon-sizes.py --source-dir /path/to/csv
This records source heights, not life-size rendering dimensions. Runtime sizing
compresses them to a small visual range in PokemonDisplaySize.
"""
import argparse
import csv
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REGIONAL = {
    4052: "meowth-galar", 4083: "farfetchd-galar", 4122: "mr-mime-galar",
    4222: "corsola-galar", 4263: "zigzagoon-galar", 4264: "linoone-galar",
    4562: "yamask-galar", 6211: "qwilfish-hisui", 6215: "sneasel-hisui",
    6550: "basculin-white-striped", 8194: "wooper-paldea",
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-dir", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, default=ROOT / "Assets" / "Data")
    args = parser.parse_args()
    source = args.source_dir / "pokemon.csv"
    manifest = json.loads((ROOT / "Assets" / "Data" / "sources.json").read_text())
    if hashlib.sha256(source.read_bytes()).hexdigest() != manifest["Sha256"]["pokemon.csv"]:
        raise ValueError("pokemon.csv differs from the pinned source manifest")
    with source.open(encoding="utf-8", newline="") as file:
        rows = list(csv.DictReader(file))
    heights = {int(r["species_id"]): int(r["height"]) for r in rows
               if r["is_default"] == "1" and int(r["species_id"]) <= 1025}
    by_name = {r["identifier"]: r for r in rows}
    heights.update({dex: int(by_name[name]["height"]) for dex, name in REGIONAL.items()})
    assert set(heights) == set(range(1, 1026)) | set(REGIONAL)
    assert all(height > 0 for height in heights.values())
    args.output_dir.mkdir(parents=True, exist_ok=True)
    output = {"SourceCommit": manifest["Commit"], "Unit": "decimeter",
              "Heights": dict(sorted(heights.items()))}
    (args.output_dir / "pokemon-sizes.json").write_text(
        json.dumps(output, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Generated {len(heights)} size references from the pinned pokemon.csv")


if __name__ == "__main__":
    main()
