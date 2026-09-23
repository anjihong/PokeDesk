using System.Text.Json;
using Xunit;

namespace DeskPokemon.Tests;

public class ModelTests
{
    [Theory]
    [InlineData(0, EggKind.Common)]
    [InlineData(.474999, EggKind.Common)]
    [InlineData(.475, EggKind.Rare)]
    [InlineData(.774999, EggKind.Rare)]
    [InlineData(.775, EggKind.Epic)]
    [InlineData(.924999, EggKind.Epic)]
    [InlineData(.925, EggKind.Legendary)]
    [InlineData(.974999, EggKind.Legendary)]
    [InlineData(.975, EggKind.Shiny)]
    [InlineData(.999999, EggKind.Shiny)]
    public void EggTiersRespectProbabilityBoundaries(double roll, EggKind expected) =>
        Assert.Equal(expected, EggHatcher.RollKind(new FixedRandom(roll)));

    [Theory]
    [InlineData(EggKind.Rare, .149999, true)]
    [InlineData(EggKind.Rare, .15, false)]
    [InlineData(EggKind.Epic, .499999, true)]
    [InlineData(EggKind.Epic, .5, false)]
    public void RareAndEpicChooseTheSpecialPoolAtTheirOwnBoundary(EggKind kind, double roll, bool special)
    {
        var egg = EggHatcher.Create(kind, rng: new FixedRandom(roll));
        Assert.Equal(special, PokemonRarity.IsSpecial(egg.Dex));
    }

    [Theory]
    [InlineData(EggKind.Common)]
    [InlineData(EggKind.Rare)]
    [InlineData(EggKind.Epic)]
    [InlineData(EggKind.Legendary)]
    [InlineData(EggKind.Shiny)]
    public void ShinyChanceIsSevenPercentUnlessTheTierOrDebugOverrideGuaranteesIt(EggKind kind)
    {
        Assert.True(EggHatcher.Create(kind, rng: new FixedRandom(.069999)).IsShiny);
        Assert.Equal(kind == EggKind.Shiny, EggHatcher.Create(kind, rng: new FixedRandom(.07)).IsShiny);
        Assert.Equal(kind == EggKind.Shiny, EggHatcher.Create(kind, rng: new FixedRandom(.999999)).IsShiny);
        Assert.True(EggHatcher.Create(kind, forceShiny: true, rng: new FixedRandom(.999999)).IsShiny);
    }

    [Fact]
    public void TierGroupAndColorUseIndependentRandomDrawsInOrder()
    {
        var random = new SequenceRandom([.475, .149999, .069999]);
        var egg = EggHatcher.Create(rng: random);

        Assert.Equal(EggKind.Rare, egg.Kind);
        Assert.True(PokemonRarity.IsSpecial(egg.Dex));
        Assert.True(egg.IsShiny);
        Assert.Equal(3, random.DoubleCalls);
        Assert.Equal(1, random.IndexCalls);
    }

    [Theory]
    [InlineData(EggKind.Common)]
    [InlineData(EggKind.Legendary)]
    [InlineData(EggKind.Shiny)]
    public void EveryBaseSpeciesInTheChosenPoolHasExactlyOneUniformIndex(EggKind kind)
    {
        var expected = kind switch
        {
            EggKind.Common => BaseSpecies.Dex.Where(d => !PokemonRarity.IsSpecial(d)).ToArray(),
            EggKind.Legendary => BaseSpecies.Dex.Where(PokemonRarity.IsSpecial).ToArray(),
            _ => BaseSpecies.Dex
        };
        var actual = new List<int>();
        for (var index = 0; index < expected.Length; index++)
        {
            var random = new FixedRandom(.99, index);
            actual.Add(EggHatcher.Create(kind, rng: random).Dex);
            // The RNG bound must be species count, not capture-rate or rarity weight totals.
            Assert.Equal(expected.Length, random.LastIndexBound);
        }
        Assert.Equal(expected, actual);
        Assert.Equal(actual.Count, actual.Distinct().Count());
    }

    [Theory]
    [InlineData(150, true)] // Legendary.
    [InlineData(151, true)] // Mythical.
    [InlineData(1025, true)] // Pecharunt.
    [InlineData(984, true)] // Paradox.
    [InlineData(1023, true)] // DLC Paradox.
    [InlineData(793, false)] // Ultra Beasts remain ordinary.
    [InlineData(25, false)]
    public void SpecialClassificationIncludesLegendariesMythicalsAndParadox(int dex, bool special) =>
        Assert.Equal(special, PokemonRarity.IsSpecial(dex));

    [Fact]
    public void PreviewSupportsGrowthAndHatchingWithoutAStoragePath()
    {
        var settings = Settings.NewPreview(4);
        Assert.Equal(2, settings.SchemaVersion);
        Assert.NotNull(settings.PendingEgg);
        Assert.Equal(new[] { 4 }, settings.Owned);
        Assert.Empty(settings.ShinyOwned);

        settings.AddExp(4);
        settings.PendingEgg = new PendingEgg(EggKind.Shiny, 172, true);
        settings.Eggs = 1;
        var result = settings.Hatch()!.Value;
        settings.Save();

        Assert.True(result.IsShiny);
        Assert.True(settings.IsOwned(172, true));
        Assert.False(settings.IsOwned(172));
    }

    [Fact]
    public void NewGameImmediatelyPersistsTheWaitingEggAndDoesNotRerollOnLoad()
    {
        using var files = new SaveFiles();
        var path = files.PathFor("nested/settings.json");
        var settings = Settings.NewAt(906, path);
        var original = File.ReadAllText(path);
        var loaded = Settings.LoadFrom(path)!;

        Assert.Equal(906, loaded.SelectedDex);
        Assert.Equal(settings.PendingEgg, loaded.PendingEgg);
        Assert.Equal(0, loaded.Eggs);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void TimerNeverChangesTheCommittedRewardAndPausesWhenReady()
    {
        var settings = Settings.NewPreview(4);
        var pending = settings.PendingEgg;

        Assert.False(settings.TickEgg(Settings.EggIntervalSeconds - 1));
        Assert.True(settings.TickEgg(1));
        Assert.False(settings.TickEgg(5_000));

        Assert.Same(pending, settings.PendingEgg);
        Assert.Equal(1, settings.Eggs);
        Assert.Equal(0, settings.EggSeconds);
        Assert.Equal(0, settings.RemainingEggSeconds);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidTimerInputDoesNotChangeEggState(double seconds)
    {
        var settings = Settings.NewPreview(4);
        settings.EggSeconds = 42;
        var pending = settings.PendingEgg;

        Assert.Throws<ArgumentOutOfRangeException>(() => settings.TickEgg(seconds));
        Assert.Equal(42, settings.EggSeconds);
        Assert.Equal(0, settings.Eggs);
        Assert.Same(pending, settings.PendingEgg);
    }

    [Fact]
    public void NormalAndShinyOwnershipExperienceAndDuplicateLevelsStayIndependent()
    {
        var settings = Settings.NewPreview(4);
        Assert.True(settings.AddOwned(25, true));
        Assert.False(settings.IsOwned(25));
        Assert.True(settings.IsOwned(25, true));
        Assert.True(settings.AddOwned(25));
        settings.For(25).Exp = 3;
        settings.For(25, true).Exp = 29;

        Assert.True(settings.AddExp(25, true));
        Assert.Equal((2, 0), (settings.For(25, true).Level, settings.For(25, true).Exp));
        settings.For(25, true).Exp = 7;
        Assert.False(settings.AddOwned(25, true));
        Assert.Equal((3, 7), (settings.For(25, true).Level, settings.For(25, true).Exp));
        Assert.Equal((1, 3), (settings.For(25).Level, settings.For(25).Exp));
    }

    [Fact]
    public void FirstShinyHatchIsNewEvenWhenTheNormalPokemonIsAlreadyOwned()
    {
        var settings = Settings.NewPreview(4);
        settings.For(4).Level = 9;
        settings.For(4).Exp = 17;
        settings.PendingEgg = new PendingEgg(EggKind.Common, 4, true);
        settings.Eggs = 1;

        Assert.Equal(new HatchResult(4, true, true, 1), settings.Hatch()!.Value);
        Assert.Equal((9, 17), (settings.For(4).Level, settings.For(4).Exp));
        Assert.True(settings.IsOwned(4));
        Assert.True(settings.IsOwned(4, true));
    }

    [Fact]
    public void SelectedShinyAndBothProgressRecordsSurviveSavingAndReloading()
    {
        using var files = new SaveFiles();
        var path = files.PathFor("settings.json");
        var settings = Settings.NewAt(4, path);
        settings.AddOwned(25);
        settings.AddOwned(25, true);
        settings.For(25).Level = 3;
        settings.For(25).Exp = 8;
        settings.For(25, true).Level = 7;
        settings.For(25, true).Exp = 12;
        settings.SelectedDex = 25;
        settings.SelectedShiny = true;
        settings.Save();

        var loaded = Settings.LoadFrom(path)!;
        Assert.True(loaded.SelectedShiny);
        Assert.Equal(25, loaded.SelectedDex);
        Assert.True(loaded.IsOwned(25));
        Assert.True(loaded.IsOwned(25, true));
        Assert.Equal((3, 8), (loaded.For(25).Level, loaded.For(25).Exp));
        Assert.Equal((7, 12), (loaded.For(25, true).Level, loaded.For(25, true).Exp));
    }

    [Theory]
    [InlineData(EggKind.Common)]
    [InlineData(EggKind.Rare)]
    [InlineData(EggKind.Epic)]
    [InlineData(EggKind.Legendary)]
    [InlineData(EggKind.Shiny)]
    public void HatchUsesSavedRewardAndAtomicallyPersistsOwnershipAndTheNextEgg(EggKind kind)
    {
        using var files = new SaveFiles();
        var path = files.PathFor("settings.json");
        var settings = Settings.NewAt(4, path);
        var dex = kind == EggKind.Legendary ? 150 : 172;
        var pending = new PendingEgg(kind, dex, true);
        settings.PendingEgg = pending;
        settings.Eggs = 1;
        settings.Save();
        var resumed = Settings.LoadFrom(path)!;

        var result = resumed.Hatch()!.Value;
        Assert.Equal(new HatchResult(dex, true, true, 1), result);
        Assert.Null(resumed.Hatch());
        Assert.NotNull(resumed.PendingEgg);
        var reloaded = Settings.LoadFrom(path)!;
        Assert.True(reloaded.IsOwned(dex, true));
        Assert.False(reloaded.IsOwned(dex));
        Assert.Equal(resumed.PendingEgg, reloaded.PendingEgg);
        Assert.Equal(0, reloaded.Eggs);
        Assert.Equal(0, reloaded.EggSeconds);
        Assert.Empty(Directory.EnumerateFiles(files.DirectoryPath, "*.tmp"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateHatchIncrementsOnlyTheRewardColorAndRetainsExperience(bool shiny)
    {
        var settings = Settings.NewPreview(4);
        settings.AddOwned(172, shiny);
        settings.AddOwned(172, !shiny);
        settings.For(172, shiny).Exp = 9;
        settings.For(172, !shiny).Level = 6;
        settings.PendingEgg = new PendingEgg(EggKind.Common, 172, shiny);
        settings.Eggs = 1;

        Assert.Equal(new HatchResult(172, shiny, false, 2), settings.Hatch()!.Value);
        Assert.Equal((2, 9), (settings.For(172, shiny).Level, settings.For(172, shiny).Exp));
        Assert.Equal((6, 0), (settings.For(172, !shiny).Level, settings.For(172, !shiny).Exp));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void LegacyMigrationBacksUpOriginalAndPersistsAStableWaitingOrReadyReward(int eggs)
    {
        using var files = new SaveFiles();
        var path = files.PathFor("settings.json");
        var original = $$$"""
            {"SchemaVersion":1,"StarterDex":4,"SelectedDex":7,"Owned":[4,7],"Progress":{"7":{"Level":8,"Exp":12}},"Eggs":{{{eggs}}},"EggSeconds":93}
            """;
        File.WriteAllText(path, original);

        var migrated = Settings.LoadFrom(path)!;
        Assert.Equal(2, migrated.SchemaVersion);
        Assert.Equal(eggs, migrated.Eggs);
        Assert.Equal(93, migrated.EggSeconds);
        Assert.NotNull(migrated.PendingEgg);
        if (eggs == 1) Assert.Equal(EggKind.Common, migrated.PendingEgg.Kind);
        Assert.Equal(7, migrated.SelectedDex);
        Assert.False(migrated.SelectedShiny);
        Assert.Empty(migrated.ShinyOwned);
        Assert.Equal((8, 12), (migrated.For(7).Level, migrated.For(7).Exp));
        Assert.Equal(original, File.ReadAllText(path + ".schema1.bak"));

        var persisted = File.ReadAllText(path);
        var timestamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, timestamp);
        var unchangedTimestamp = File.GetLastWriteTimeUtc(path);
        var loadedAgain = Settings.LoadFrom(path)!;
        Assert.Equal(migrated.PendingEgg, loadedAgain.PendingEgg);
        Assert.Equal(persisted, File.ReadAllText(path));
        Assert.Equal(unchangedTimestamp, File.GetLastWriteTimeUtc(path));
        Assert.Equal(original, File.ReadAllText(path + ".schema1.bak"));
    }

    [Fact]
    public void SchemaZeroMigrationKeepsLegacyGrowthAndRecoversTheOriginalStarter()
    {
        using var files = new SaveFiles();
        var path = files.PathFor("settings.json");
        const string original = """{"SelectedDex":99,"Progress":{"4":{"Level":9,"Exp":6}},"EggSeconds":123}""";
        File.WriteAllText(path, original);

        var migrated = Settings.LoadFrom(path)!;
        Assert.Equal(4, migrated.StarterDex);
        Assert.Equal(4, migrated.SelectedDex);
        Assert.Equal(new[] { 4 }, migrated.Owned);
        Assert.Equal((9, 6), (migrated.For(4).Level, migrated.For(4).Exp));
        Assert.Equal(123, migrated.EggSeconds);
        Assert.Equal(original, File.ReadAllText(path + ".schema0.bak"));
        Assert.Equal(migrated.PendingEgg, Settings.LoadFrom(path)!.PendingEgg);
    }

    [Fact]
    public void ExistingMigrationBackupIsNeverOverwritten()
    {
        using var files = new SaveFiles();
        var path = files.PathFor("settings.json");
        const string firstBackup = "original backup preserved by a previous migration";
        File.WriteAllText(path + ".schema1.bak", firstBackup);
        File.WriteAllText(path, """{"SchemaVersion":1,"StarterDex":4,"SelectedDex":4,"Owned":[4],"EggSeconds":93}""");

        var migrated = Settings.LoadFrom(path)!;
        Assert.Equal(firstBackup, File.ReadAllText(path + ".schema1.bak"));
        Assert.Equal(93, migrated.EggSeconds);
        Assert.Equal(migrated.PendingEgg, Settings.LoadFrom(path)!.PendingEgg);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FailedAtomicSaveRestoresNewOrDuplicateHatchAndCleansItsTemporaryFile(bool shiny, bool duplicate)
    {
        using var files = new SaveFiles();
        var path = files.PathFor("settings.json");
        var settings = Settings.NewAt(4, path);
        settings.AddOwned(172, !shiny);
        settings.For(172, !shiny).Level = 9;
        settings.For(172, !shiny).Exp = 22;
        if (duplicate)
        {
            settings.AddOwned(172, shiny);
            settings.For(172, shiny).Level = 6;
            settings.For(172, shiny).Exp = 12;
        }
        var pending = new PendingEgg(EggKind.Common, 172, shiny);
        settings.PendingEgg = pending;
        settings.Eggs = 1;
        settings.EggSeconds = 79;
        settings.Save();
        var memoryBefore = JsonSerializer.Serialize(settings);
        var diskBefore = File.ReadAllText(path);

        // POSIX permits rename despite FileShare.None. A directory at the destination instead
        // fails the final file promotion on Windows and macOS, after the temp file was written.
        // Preserve the existing save alongside it so the test can restore and verify its bytes.
        var preserved = files.PathFor("last-good.json");
        File.Move(path, preserved);
        Directory.CreateDirectory(path);
        try
        {
            Assert.ThrowsAny<IOException>(() => settings.Hatch());
            Assert.Equal(memoryBefore, JsonSerializer.Serialize(settings));
            Assert.Same(pending, settings.PendingEgg);
            Assert.Empty(Directory.EnumerateFiles(files.DirectoryPath, "*.tmp"));
            Assert.Equal(diskBefore, File.ReadAllText(preserved));
        }
        finally
        {
            Directory.Delete(path);
            File.Move(preserved, path);
        }
        var loaded = Settings.LoadFrom(path)!;
        Assert.Equal(pending, loaded.PendingEgg);
        Assert.Equal(duplicate, loaded.IsOwned(172, shiny));
        Assert.Equal(1, loaded.Eggs);
        Assert.Equal(79, loaded.EggSeconds);
    }

    [Fact]
    public void FutureSchemaIsRejectedWithoutChangingTheFileOrCreatingABackup()
    {
        using var files = new SaveFiles();
        var path = files.PathFor("settings.json");
        const string original = """{"SchemaVersion":3,"StarterDex":4,"FutureData":{"preserve":true}}""";
        File.WriteAllText(path, original);

        Assert.Throws<InvalidDataException>(() => Settings.LoadFrom(path));
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(files.DirectoryPath));
    }

    private sealed class FixedRandom(double value, int index = 0) : Random
    {
        public int LastIndexBound { get; private set; }
        public override double NextDouble() => value;
        public override int Next(int maxValue)
        {
            LastIndexBound = maxValue;
            Assert.InRange(index, 0, maxValue - 1);
            return index;
        }
    }

    private sealed class SequenceRandom(double[] values) : Random
    {
        public int DoubleCalls { get; private set; }
        public int IndexCalls { get; private set; }
        public override double NextDouble() => values[DoubleCalls++];
        public override int Next(int maxValue)
        {
            IndexCalls++;
            return 0;
        }
    }

    private sealed class SaveFiles : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "PokeDesk-model-tests-" + Guid.NewGuid().ToString("N"));
        public SaveFiles() => Directory.CreateDirectory(DirectoryPath);
        public string PathFor(string name) => Path.Combine(DirectoryPath, name);
        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
