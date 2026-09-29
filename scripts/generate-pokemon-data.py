#!/usr/bin/env python3
"""Rebuild offline species/evolution data from an immutable PokeAPI CSV snapshot.

python3 scripts/generate-pokemon-data.py --download
python3 scripts/generate-pokemon-data.py --source-dir /path/to/pinned/csv
Evolution rules come from tools/generate_evolutions.py.
No third-party Python packages are needed. Existing CSVs are hash-checked when a
source manifest exists, so regeneration cannot silently use a different version.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import urllib.request

ROOT = Path(__file__).resolve().parents[1]

# Keep main's generator as the only source of evolution rules, supported forms,
# and source revision. Importing it does not execute its download/write CLI.
_spec = importlib.util.spec_from_file_location("pokedesk_evolutions", ROOT / "tools" / "generate_evolutions.py")
assert _spec is not None and _spec.loader is not None
EVOLUTIONS = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(EVOLUTIONS)
COMMIT = EVOLUTIONS.COMMIT
SOURCE = EVOLUTIONS.ROOT
REGIONAL = EVOLUTIONS.REGIONAL
FILES = ("pokemon_forms.csv", "pokemon_species.csv", "pokemon_evolution.csv", "pokemon.csv", "pokemon_types.csv",
         "type_names.csv", "pokemon_species_flavor_text.csv", "versions.csv", "pokemon_species_names.csv")


def generate_evolutions(read):
    # Supply hash-checked local CSVs while preserving main's unchanged generator.
    original_read = EVOLUTIONS.read
    try:
        EVOLUTIONS.read = read
        return EVOLUTIONS.generate()
    finally:
        EVOLUTIONS.read = original_read


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
        if previous["Commit"] == COMMIT and any(hashes.get(name) != digest for name, digest in previous["Sha256"].items()):
            raise ValueError("CSV content differs from the pinned source manifest")

    def rows(name: str) -> list[dict[str, str]]:
        with (args.source_dir / name).open(encoding="utf-8", newline="") as file:
            return list(csv.DictReader(file))

    species = {int(row["id"]): row for row in rows("pokemon_species.csv") if int(row["id"]) <= 1025}
    assert set(species) == set(range(1, 1026))
    evolution_catalog = generate_evolutions(lambda name: rows(name + ".csv"))

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

    # Regional appearances keep their own authoritative types. The species flavor text
    # may describe a different form, so provide sourced genus/type metadata instead.
    pokemon = {int(row["id"]): row for row in rows("pokemon.csv")}
    forms = {row["identifier"]: row for row in rows("pokemon_forms.csv")}
    for key, (identifier, dex, generation, name) in sorted(REGIONAL.items()):
        pokemon_id = int(forms[identifier]["pokemon_id"])
        assert int(pokemon[pokemon_id]["species_id"]) == dex
        types = [type_names[int(row["type_id"])] for row in sorted(type_rows, key=lambda row: int(row["slot"]))
                 if int(row["pokemon_id"]) == pokemon_id]
        assert 1 <= len(types) <= 2
        details.append({"Dex": key, "Description": f"분류: {korean_names[dex]['genus']}\n타입: {' / '.join(types)}",
                        "Types": types, "FlavorVersion": None, "MetadataOnly": True})
        metadata_only.append(key)

    args.output_dir.mkdir(parents=True, exist_ok=True)
    def write(name: str, value: object) -> None:
        (args.output_dir / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    write("evolutions.json", evolution_catalog)
    write("pokemon-details.json", details)
    write("sources.json", {"Repository": "https://github.com/PokeAPI/pokeapi", "Commit": COMMIT,
                           "CsvBaseUrl": SOURCE, "Sha256": hashes, "MetadataOnlyDex": metadata_only,
                           "EvolutionRules": "Supported default and 11 regional forms; pinned PokeAPI default evolution rows; non-level first step25/second step40; branch level overrides79=37,133=25,281=30,290=20,361=42; base appearances only in new egg pool; 8 legacy Eevee evolution eggs remain valid in saves."})
    print(f"Generated {len(evolution_catalog['rules'])} evolution steps, {len(details)} details ({len(metadata_only)} use sourced Korean genus/type metadata; no Korean flavor text upstream).")


if __name__ == "__main__":
    main()
