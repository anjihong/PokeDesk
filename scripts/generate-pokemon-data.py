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
FILES = ("pokemon_forms.csv", "pokemon_species.csv", "pokemon_evolution.csv", "pokemon.csv", "pokemon_types.csv",
         "type_names.csv", "pokemon_species_flavor_text.csv", "versions.csv", "pokemon_species_names.csv")
ROOT = Path(__file__).resolve().parents[1]


REGIONAL = {
    4052: ("meowth-galar", 52, 8, "가라르 나옹"),
    4083: ("farfetchd-galar", 83, 8, "가라르 파오리"),
    4122: ("mr-mime-galar", 122, 8, "가라르 마임맨"),
    4222: ("corsola-galar", 222, 8, "가라르 코산호"),
    4263: ("zigzagoon-galar", 263, 8, "가라르 지그제구리"),
    4264: ("linoone-galar", 264, 8, "가라르 직구리"),
    4562: ("yamask-galar", 562, 8, "가라르 데스마스"),
    6211: ("qwilfish-hisui", 211, 8, "히스이 침바루"),
    6215: ("sneasel-hisui", 215, 8, "히스이 포푸니"),
    6550: ("basculin-white-striped", 550, 8, "흰줄무늬 배쓰나이"),
    8194: ("wooper-paldea", 194, 9, "팔데아 우파"),
}
OVERRIDES = {79: 37, 133: 25, 281: 30, 290: 20, 361: 42}


def generate_evolutions(read):
    species = {int(r["id"]): r for r in read("pokemon_species") if int(r["id"]) <= 1025}
    pokemon = {int(r["id"]): r for r in read("pokemon")}
    forms = read("pokemon_forms")
    supported = {}
    for form in forms:
        p = pokemon[int(form["pokemon_id"])]
        dex = int(p["species_id"])
        if dex in species and p["is_default"] == "1" and form["is_default"] == "1":
            assert dex not in supported, ("multiple default forms", dex)
            supported[dex] = int(form["id"])
    for key, (name, _, _, _) in REGIONAL.items():
        supported[key] = int(next(f for f in forms if f["identifier"] == name)["id"])
    assert len(supported) == 1036
    by_form = {form: key for key, form in supported.items()}
    grouped = {}
    for row in read("pokemon_evolution"):
        dex = int(row["evolved_species_id"])
        if dex not in species:
            continue
        parent = int(species[dex]["evolves_from_species_id"])
        source = by_form.get(int(row["required_pokemon_form_id"])) if row["required_pokemon_form_id"] else parent
        target = by_form.get(int(row["evolved_pokemon_form_id"])) if row["evolved_pokemon_form_id"] else dex
        # 원본에는 meadow 연결이 없다. 비비용의 지역별 무늬는 모두 같은 종·레벨이며 앱은 meadow만 표시한다.
        if dex == 666:
            source, target = 665, 666
        if source is None or target is None:
            continue
        grouped.setdefault((source, target), []).append(row)
    parents = {target: source for source, target in grouped}
    assert len(parents) == len(grouped), "multiple parents for one supported form"

    def depth(key, seen=frozenset()):
        assert key not in seen, "evolution cycle"
        return 0 if key not in parents else 1 + depth(parents[key], seen | {key})

    rules = []
    for (source, target), rows in sorted(grouped.items()):
        default = [r for r in rows if r["is_default"] == "1"]
        assert default, ("no default evolution", source, target)
        levels = {int(r["minimum_level"]) if r["minimum_level"] else None for r in default}
        assert len(levels) == 1, ("conflicting levels", source, target, levels)
        original = levels.pop()
        level = original or (25 if depth(target) == 1 else 40)
        kind = "pokeapi" if original else "fallback"
        if source in OVERRIDES:
            level, kind = OVERRIDES[source], "override"
        rules.append(dict(fromId=source, toId=target, level=level, levelSource=kind,
                          originalLevel=original, sourceRows=sorted(int(r["id"]) for r in default)))
    roots = {dex for dex, row in species.items() if not row["evolves_from_species_id"]}
    roots |= REGIONAL.keys() - parents.keys()
    reachable = set(roots)
    while True:
        expanded = reachable | {r["toId"] for r in rules if r["fromId"] in reachable}
        if expanded == reachable:
            break
        reachable = expanded
    assert reachable == supported.keys(), ("unreachable species", supported.keys() - reachable)
    for key in supported:
        assert depth(key) <= 2, ("unsupported evolution depth", key)
    assert next(r["level"] for r in rules if r["toId"] == 6) == 36
    assert (194, 980) not in grouped and (8194, 980) in grouped
    return dict(sourceCommit=COMMIT,
                forms=[dict(id=key, dex=dex, generation=gen, name=name, spriteKey=str(key))
                       for key, (_, dex, gen, name) in sorted(REGIONAL.items())],
                eggPool=sorted(roots), rules=rules)


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
