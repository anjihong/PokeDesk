using DeskPokemon;
using System.Text.Json;

var checks = 0;
void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
    checks++;
}

var boundaries = new (double Roll, EggKind Kind)[]
{
    (0, EggKind.Common), (.474999, EggKind.Common), (.475, EggKind.Rare),
    (.774999, EggKind.Rare), (.775, EggKind.Epic), (.924999, EggKind.Epic),
    (.925, EggKind.Legendary), (.974999, EggKind.Legendary), (.975, EggKind.Shiny), (.999999, EggKind.Shiny)
};
foreach (var (roll, kind) in boundaries)
    Check(EggHatcher.RollKind(new FixedRandom(roll)) == kind, $"grade boundary {roll}");
foreach (var (kind, boundary) in new[] { (EggKind.Rare, .15), (EggKind.Epic, .5) })
{
    Check(PokemonRarity.IsSpecial(EggHatcher.Create(kind, rng: new FixedRandom(boundary - .00001)).Dex), "special below boundary");
    Check(!PokemonRarity.IsSpecial(EggHatcher.Create(kind, rng: new FixedRandom(boundary)).Dex), "ordinary at boundary");
}
foreach (var kind in Enum.GetValues<EggKind>())
{
    var egg = EggHatcher.Create(kind, rng: new FixedRandom(.069999));
    Check(egg.IsShiny, "7% lower boundary");
    Check(EggHatcher.Create(kind, rng: new FixedRandom(.07)).IsShiny == (kind == EggKind.Shiny), "7% exact boundary");
    Check(EggHatcher.Create(kind, true, new FixedRandom(.99)).IsShiny, "forced shiny");
}
var ordinary = BaseSpecies.Dex.Where(d => !PokemonRarity.IsSpecial(d)).ToArray();
var special = BaseSpecies.Dex.Where(PokemonRarity.IsSpecial).ToArray();
Check(ordinary.Intersect(special).Count() == 0 && ordinary.Concat(special).Order().SequenceEqual(BaseSpecies.Dex), "partition");
foreach (var (kind, pool) in new[] { (EggKind.Common, ordinary), (EggKind.Legendary, special), (EggKind.Shiny, BaseSpecies.Dex) })
{
    for (var index = 0; index < pool.Length; index++)
        Check(EggHatcher.Create(kind, rng: new FixedRandom(.99, index)).Dex == pool[index], "every uniform index reachable");
}
Check(PokemonRarity.IsSpecial(1025) && PokemonRarity.IsSpecial(1023) && !PokemonRarity.IsSpecial(793), "mythical/paradox/ultra beast classification");
var directory = Path.Combine(Path.GetTempPath(), "DeskPokemon-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var path = Path.Combine(directory, "settings.json");
    var futurePath = Path.Combine(directory, "future.json");
    const string futureJson = """{"SchemaVersion":3,"StarterDex":4,"FutureData":{"preserve":true}}""";
    File.WriteAllText(futurePath, futureJson);
    var futureRejected = false;
    try { Settings.LoadFrom(futurePath); }
    catch (InvalidDataException) { futureRejected = true; }
    Check(futureRejected, "future schema rejected");
    Check(File.ReadAllText(futurePath) == futureJson && !File.Exists(futurePath + ".schema3.bak"), "future schema original unchanged");
    var legacy = """{"SchemaVersion":1,"StarterDex":4,"SelectedDex":7,"Owned":[4,7],"Progress":{"7":{"Level":8,"Exp":12}},"Eggs":1,"EggSeconds":93}""";
    File.WriteAllText(path, legacy);
    var s = Settings.LoadFrom(path)!;
    Check(s.SchemaVersion == 2 && s.PendingEgg!.Kind == EggKind.Common && s.Eggs == 1, "ready legacy egg becomes common");
    Check(s.SelectedDex == 7 && !s.SelectedShiny && s.For(7).Level == 8 && s.For(7).Exp == 12 && s.EggSeconds == 93, "legacy data preserved");
    Check(File.ReadAllText(path + ".schema1.bak") == legacy, "exact original backup");
    Check(s.ShinyOwned.Count == 0, "no gifted shinies");
    var savedEgg = s.PendingEgg;
    Check(Settings.LoadFrom(path)!.PendingEgg == savedEgg, "migration persisted and stable");
    var result = s.Hatch()!.Value;
    Check(result.Dex == savedEgg!.Dex && result.IsShiny == savedEgg.IsShiny, "hatch uses precommitted result");
    Check(s.Eggs == 0 && s.Hatch() is null, "single consumption");
    Check(Settings.LoadFrom(path)!.IsOwned(result.Dex, result.IsShiny), "hatch registration persisted");
    var waiting = s.PendingEgg;
    Check(!s.TickEgg(1799) && s.TickEgg(1) && s.PendingEgg == waiting, "timer never rerolls");
    Check(!s.TickEgg(1800) && s.EggSeconds == 0, "ready timer pauses");
    s.AddOwned(25);
    s.AddOwned(25, true);
    s.For(25).Exp = 3;
    s.For(25, true).Exp = 5;
    Check(!s.AddOwned(25, true) && s.For(25, true).Level == 2 && s.For(25).Level == 1, "duplicate levels separated");
    s.AddExp(25, true);
    Check(s.For(25).Exp == 3 && s.For(25, true).Exp == 6, "experience separated");
    s.SelectedDex = 25;
    s.SelectedShiny = true;
    s.Save();
    Check(Settings.LoadFrom(path)!.SelectedShiny, "selected color roundtrip");
#if DEBUG
    foreach (var kind in Enum.GetValues<EggKind>())
    {
        s.GrantTestEgg(kind, true);
        s.Save();
        var reload = Settings.LoadFrom(path)!;
        Check(reload.Eggs == 1 && reload.EggSeconds == 0 && reload.PendingEgg!.Kind == kind && reload.PendingEgg.IsShiny, "test egg persisted");
        Check(reload.Hatch()!.Value.IsShiny, "forced shiny hatch");
    }
#else
    Check(typeof(Settings).GetMethod("GrantTestEgg") is null, "Release has no test grant entry point");
#endif
    File.WriteAllText(path, legacy.Replace("\"Eggs\":1", "\"Eggs\":0"));
    var waitingMigration = Settings.LoadFrom(path)!;
    Check(waitingMigration.EggSeconds == 93 && waitingMigration.Eggs == 0, "waiting migration preserves time with existing backup");
    var stable = waitingMigration.PendingEgg;
    Check(Settings.LoadFrom(path)!.PendingEgg == stable, "waiting migration stable");
    var oldPath = Path.Combine(directory, "schema0.json");
    File.WriteAllText(oldPath, """{"SelectedDex":99,"Progress":{"4":{"Level":9,"Exp":6}},"EggSeconds":123}""");
    var old = Settings.LoadFrom(oldPath)!;
    Check(old.StarterDex == 4 && old.SelectedDex == 4 && !old.SelectedShiny && old.Owned.SetEquals([4]), "schema0 starter and selected recovery");
    Check(old.For(4).Level == 9 && old.For(4).Exp == 6 && old.EggSeconds == 123, "schema0 progress preserved");
    Check(File.Exists(oldPath + ".schema0.bak"), "schema0 backup");
    var fresh = Settings.NewAt(4, Path.Combine(directory, "fresh.json"));
    Check(fresh.PendingEgg is not null && Settings.LoadFrom(Path.Combine(directory, "fresh.json"))!.PendingEgg == fresh.PendingEgg, "new game immediately persisted");
    // Force atomic replacement failure with an existing save held exclusively.
    s = Settings.LoadFrom(path)!;
    s.Eggs = 1;
    s.PendingEgg = new PendingEgg(EggKind.Common, 25, true);
    var wasOwned = s.ShinyOwned.Contains(25);
    var levelBefore = s.For(25, true).Level;
    var pendingBefore = s.PendingEgg;
    var diskBefore = File.ReadAllText(path);
    using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
    {
        var failed = false;
        try { s.Hatch(); } catch (IOException) { failed = true; }
        Check(failed, "locked save fails");
    }
    Check(s.Eggs == 1 && s.PendingEgg == pendingBefore && s.ShinyOwned.Contains(25) == wasOwned && s.For(25, true).Level == levelBefore, "failed hatch rolls back memory");
    Check(File.ReadAllText(path) == diskBefore && Directory.GetFiles(directory, "*.tmp").Length == 0, "failed save preserves original and removes temp");
}
finally { Directory.Delete(directory, recursive: true); }
Console.WriteLine($"PASS: {checks} model assertions");

sealed class FixedRandom(double value, int index = 0) : Random
{
    public override double NextDouble() => value;
    public override int Next(int maxValue) => index < maxValue ? index : throw new Exception("index outside pool");
}
