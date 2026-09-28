#!/usr/bin/env python3
"""Rebuild offline species/evolution data from an immutable PokeAPI CSV snapshot.

python3 scripts/generate-pokemon-data.py --download
python3 scripts/generate-pokemon-data.py --source-dir /path/to/pinned/csv
No third-party Python packages are needed. Existing CSVs are hash-checked when a
source manifest exists, so regeneration cannot silently use a different version.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
from pathlib import Path
import tempfile
import urllib.request

COMMIT = "168b1e89467054cda2e7df43ccebbb69b459497a"
SOURCE = f"https://raw.githubusercontent.com/PokeAPI/pokeapi/{COMMIT}/data/v2/csv/"
FILES = ("pokemon_species.csv", "pokemon_evolution.csv", "pokemon.csv", "pokemon_types.csv",
         "type_names.csv", "pokemon_species_flavor_text.csv", "versions.csv", "pokemon_species_names.csv")
ROOT = Path(__file__).resolve().parents[1]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-dir", type=Path, default=Path(tempfile.gettempdir()) / "pokedesk-pokeapi" / COMMIT)
    parser.add_argument("--output-dir", type=Path, default=ROOT / "Assets" / "Data")
    parser.add_argument("--download", action="store_true")
    args = parser.parse_args()
    args.source_dir.mkdir(parents=True, exist_ok=True)
    if args.download:
        for name in FILES:
            with urllib.request.urlopen(SOURCE + name, timeout=60) as response:
                (args.source_dir / name).write_bytes(response.read())
    hashes = {name: hashlib.sha256((args.source_dir / name).read_bytes()).hexdigest() for name in FILES}
    manifest_path = args.output_dir / "sources.json"
    if manifest_path.exists():
        previous = json.loads(manifest_path.read_text(encoding="utf-8"))
        if previous["Commit"] == COMMIT and hashes != previous["Sha256"]:
            raise ValueError("CSV content differs from the pinned source manifest")

    def rows(name: str) -> list[dict[str, str]]:
        with (args.source_dir / name).open(encoding="utf-8", newline="") as file:
            return list(csv.DictReader(file))

    species = {int(row["id"]): row for row in rows("pokemon_species.csv") if int(row["id"]) <= 1025}
    assert set(species) == set(range(1, 1026))
    parents = {dex: int(row["evolves_from_species_id"]) for dex, row in species.items() if row["evolves_from_species_id"]}
    all_rules = rows("pokemon_evolution.csv")
    rules = []
    for dex, parent in sorted(parents.items()):
        if parent == 133:  # Eeveelutions are egg rewards, never evolution choices.
            continue
        depth, ancestor = 0, dex
        while ancestor in parents:
            ancestor = parents[ancestor]
            depth += 1
        candidates = [row for row in all_rules if int(row["evolved_species_id"]) == dex and row["is_default"] == "1"]
        # Some species have regional forms with different levels. Prefer the ordinary
        # resulting form, retaining form-only species such as Perrserker when necessary.
        ordinary = [row for row in candidates if not row["evolved_pokemon_form_id"]]
        if ordinary:
            candidates = ordinary
        levels = [int(row["minimum_level"]) for row in candidates if row["minimum_level"]]
        level = min(levels) if levels else (25 if depth == 1 else 40)
        assert 1 <= level <= 100
        rules.append({"FromDex": parent, "TargetDex": dex, "RequiredLevel": level,
                      "UsesFallback": not levels})

    korean_names = {int(row["pokemon_species_id"]): row for row in rows("pokemon_species_names.csv") if row["local_language_id"] == "3"}
    type_names = {int(row["type_id"]): row["name"] for row in rows("type_names.csv") if row["local_language_id"] == "3"}
    default_pokemon = {int(row["species_id"]): int(row["id"]) for row in rows("pokemon.csv") if row["is_default"] == "1"}
    type_rows = rows("pokemon_types.csv")
    flavor = {}
    for row in rows("pokemon_species_flavor_text.csv"):
        dex = int(row["species_id"])
        if row["language_id"] == "3" and (dex not in flavor or int(row["version_id"]) > int(flavor[dex]["version_id"])):
            flavor[dex] = row
    versions = {int(row["id"]): row["identifier"] for row in rows("versions.csv")}
    details, metadata_only = [], []
    for dex in species:
        types = [type_names[int(row["type_id"])] for row in sorted(type_rows, key=lambda row: int(row["slot"]))
                 if int(row["pokemon_id"]) == default_pokemon[dex]]
        assert 1 <= len(types) <= 2
        if dex in flavor:
            description = " ".join(flavor[dex]["flavor_text"].split())
            version = versions[int(flavor[dex]["version_id"])]
        else:
            description = f"분류: {korean_names[dex]['genus']}\n타입: {' / '.join(types)}"
            metadata_only.append(dex)
            version = None
        details.append({"Dex": dex, "Description": description, "Types": types,
                        "FlavorVersion": version, "MetadataOnly": dex not in flavor})

    args.output_dir.mkdir(parents=True, exist_ok=True)
    def write(name: str, value: object) -> None:
        (args.output_dir / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    write("evolutions.json", rules)
    write("pokemon-details.json", details)
    write("sources.json", {"Repository": "https://github.com/PokeAPI/pokeapi", "Commit": COMMIT,
                           "CsvBaseUrl": SOURCE, "Sha256": hashes, "MetadataOnlyDex": metadata_only,
                           "EvolutionRules": "Direct species parents; ordinary resulting form preferred; lowest recorded positive minimum level; non-level first step25/second step40; Eevee branches excluded."})
    print(f"Generated {len(rules)} evolution steps, {len(details)} details ({len(metadata_only)} use sourced Korean genus/type metadata; no Korean flavor text upstream).")


if __name__ == "__main__":
    main()
