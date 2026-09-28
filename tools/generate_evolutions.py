"""고정 PokéAPI CSV에서 지원 모습의 진화 규칙을 생성한다. Python 표준 라이브러리만 사용."""
import csv
import io
import json
from pathlib import Path
from urllib.request import urlopen

COMMIT = "168b1e89467054cda2e7df43ccebbb69b459497a"
ROOT = f"https://raw.githubusercontent.com/PokeAPI/pokeapi/{COMMIT}/data/v2/csv/"
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
EEVEE = {134, 135, 136, 196, 197, 470, 471, 700}
OVERRIDES = {79: 37, 281: 30, 290: 20, 361: 42}


def read(name):
    with urlopen(ROOT + name + ".csv", timeout=30) as response:
        return list(csv.DictReader(io.StringIO(response.read().decode("utf-8"))))


def generate():
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
        if dex not in species or dex in EEVEE:
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
    roots |= EEVEE | (REGIONAL.keys() - parents.keys())
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


if __name__ == "__main__":
    data = generate()
    target = Path(__file__).resolve().parents[1] / "Assets/Data/evolutions.json"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Validated {len(data['rules'])} rules, {len(data['eggPool'])} egg candidates; wrote {target}")
