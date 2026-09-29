using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace DeskPokemon.Tests;

public class EvolutionTests
{
    [Fact]
    public void CatalogHasReachableSpeciesAndRegionalFormsWithEveryLevelBoundary()
    {
        Assert.Equal(1036, EvolutionData.Count);
        Assert.Equal(11, EvolutionData.Forms.Count);
        Assert.Equal(486, EvolutionData.Rules.Count);
        Assert.Equal(550, EvolutionData.EggPool.Count);
        var reachable = EvolutionData.EggPool.ToHashSet();
        int count;
        do
        {
            count = reachable.Count;
            foreach (var rule in EvolutionData.Rules)
                if (reachable.Contains(rule.FromId)) reachable.Add(rule.ToId);
        } while (reachable.Count != count);
        Assert.Equal(EvolutionData.Count, reachable.Count);
        Assert.Equal(EvolutionData.Rules.Count, EvolutionData.Rules.Select(r => (r.FromId, r.ToId)).Distinct().Count());
        foreach (var rule in EvolutionData.Rules)
        {
            var settings = Settings.NewPreview(4);
            settings.AddOwned(rule.FromId);
            settings.For(rule.FromId).Level = rule.Level - 1;
            Assert.DoesNotContain(settings.AvailableEvolutions(rule.FromId), option => option.TargetDex == rule.ToId);
            Assert.Null(settings.Evolve(rule.FromId, rule.ToId));
            settings.For(rule.FromId).Level++;
            Assert.Contains(settings.AvailableEvolutions(rule.FromId), option => option.TargetDex == rule.ToId);
        }
    }

    [Theory]
    [InlineData(4, 5, 16)]
    [InlineData(5, 6, 36)]
    [InlineData(723, 724, 34)]
    [InlineData(172, 25, 25)]
    [InlineData(25, 26, 40)]
    [InlineData(64, 65, 40)]
    [InlineData(999, 1000, 25)]
    [InlineData(79, 199, 37)]
    [InlineData(133, 700, 25)]
    [InlineData(281, 475, 30)]
    [InlineData(290, 292, 20)]
    [InlineData(361, 478, 42)]
    [InlineData(4263, 4264, 20)]
    [InlineData(4264, 862, 35)]
    [InlineData(8194, 980, 20)]
    public void CanonicalFallbackAndBranchOverrideLevelsAreExplicit(int from, int target, int level) =>
        Assert.Equal(level, Assert.Single(EvolutionRules.OptionsFor(from), r => r.TargetDex == target).RequiredLevel);

    [Theory]
    [InlineData(264)]
    [InlineData(222)]
    [InlineData(83)]
    [InlineData(122)]
    [InlineData(211)]
    [InlineData(550)]
    public void OrdinaryAppearanceCannotEvolveIntoARegionalOnlyChild(int dex) => Assert.Empty(EvolutionData.From(dex));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneEvolutionSharesGrowthAcrossItsRunButNeverAcrossColors(bool shiny)
    {
        using var files = new SaveFiles();
        var settings = Settings.NewAt(4, files.Path);
        settings.AddOwned(4, true);
        settings.SelectedShiny = shiny;
        settings.For(4, shiny).Level = 16;
        settings.For(4, shiny).Exp = 91;
        settings.For(4, !shiny).Level = 50;
        settings.For(4, !shiny).Exp = 12;
        var pending = settings.PendingEgg;
        Assert.Equal(new EvolutionResult(4, 5, shiny, 16, 91), settings.Evolve(4, 5, shiny));
        Assert.Equal(5, settings.SelectedDex);
        Assert.Equal(shiny, settings.SelectedShiny);
        Assert.Same(settings.For(4, shiny), settings.For(5, shiny));
        Assert.False(settings.HasOwned(5, !shiny));
        Assert.False(settings.HasOwned(6, shiny));
        settings.AddExp(4, shiny);
        Assert.Equal((16, 92), (settings.For(5, shiny).Level, settings.For(5, shiny).Exp));
        Assert.Equal((50, 12), (settings.For(4, !shiny).Level, settings.For(4, !shiny).Exp));
        settings.Save();
        var loaded = Settings.LoadFrom(files.Path)!;
        Assert.Same(loaded.For(4, shiny), loaded.For(5, shiny));
        Assert.Equal((16, 92), (loaded.For(5, shiny).Level, loaded.For(5, shiny).Exp));
        Assert.Equal(pending, loaded.PendingEgg);
    }

    [Fact]
    public void EarlierAppearanceAdvertisesTheCurrentStageWithoutSkippingIt()
    {
        var settings = Settings.NewPreview(4);
        settings.For(4).Level = 16;
        settings.For(4).Exp = 17;
        Assert.NotNull(settings.Evolve(4, 5));
        settings.SelectedDex = 4;
        settings.For(4).Level = 36;
        Assert.Empty(settings.AvailableEvolutions(4));
        Assert.Null(settings.Evolve(4, 6));
        Assert.Equal(5, Assert.Single(settings.EvolutionOptions(4)).FromId);
        Assert.Throws<InvalidOperationException>(() => settings.PrepareEvolution(4));
        Assert.Equal(36, settings.For(5).Level);
        Assert.Equal(new EvolutionResult(5, 6, false, 36, 17), settings.Evolve(5, 6));
        Assert.Same(settings.For(4), settings.For(6));
        Assert.Equal(new[] { 4, 5, 6 }, settings.For(4).History);
        Assert.Empty(settings.EvolutionOptions(4));
        Assert.Null(settings.Evolve(5, 6));
    }

    [Fact]
    public void RandomBranchIsPersistedBeforeArtworkAndNeverRerolledAfterRestart()
    {
        using var files = new SaveFiles();
        var settings = Settings.NewAt(236, files.Path);
        settings.For(236).Level = 20;
        Assert.Equal(107, settings.PrepareEvolution(236, random: new IndexRandom(1)));
        var loaded = Settings.LoadFrom(files.Path)!;
        Assert.Equal(107, loaded.PrepareEvolution(236, random: new NoRandomAllowed()));
        loaded.CompleteEvolution(236, false, 107);
        Assert.True(Hatch(loaded, 236).IsRestart);
        loaded.For(236).Level = 20;
        Assert.Equal(new[] { 106, 237 }, loaded.AvailableEvolutions(236).Select(r => r.TargetDex));
        Assert.Equal(237, loaded.PrepareEvolution(236, random: new IndexRandom(1)));
        loaded.CompleteEvolution(236, false, 237);
        Assert.True(Hatch(loaded, 236).IsRestart);
        loaded.For(236).Level = 20;
        Assert.Equal(106, loaded.PrepareEvolution(236, random: new IndexRandom(0)));
        loaded.CompleteEvolution(236, false, 106);
        Assert.False(Hatch(loaded, 236).IsRestart);
        Assert.Equal(21, loaded.For(106).Level);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RehatchingABranchedRootStartsANewRunAndRebindsItsIntermediateOnlyWhenReached(bool shiny)
    {
        var settings = Settings.NewPreview(280);
        if (shiny) settings.AddOwned(280, true);
        settings.For(280, shiny).Level = 20;
        Assert.NotNull(settings.Evolve(280, 281, shiny));
        settings.For(281, shiny).Level = 30;
        Assert.NotNull(settings.Evolve(281, 282, shiny));
        var gardevoir = settings.For(282, shiny);
        gardevoir.Level = 34;
        gardevoir.Exp = 9;
        Assert.True(Hatch(settings, 280, shiny).IsRestart);
        Assert.Equal((1, 0), (settings.For(280, shiny).Level, settings.For(280, shiny).Exp));
        Assert.Same(gardevoir, settings.For(281, shiny));
        Assert.Equal((34, 9), (gardevoir.Level, gardevoir.Exp));
        Assert.False(Hatch(settings, 280, shiny).IsRestart);
        Assert.Equal(2, settings.For(280, shiny).Level);
        settings.For(280, shiny).Level = 20;
        Assert.NotNull(settings.Evolve(280, 281, shiny));
        Assert.NotSame(gardevoir, settings.For(281, shiny));
        settings.For(281, shiny).Level = 30;
        Assert.Equal(475, Assert.Single(settings.AvailableEvolutions(281, shiny)).TargetDex);
        Assert.NotNull(settings.Evolve(281, 475, shiny));
        Assert.False(Hatch(settings, 280, shiny).IsRestart);
        Assert.Equal(31, settings.For(475, shiny).Level);
        Assert.Equal((34, 9), (gardevoir.Level, gardevoir.Exp));
        Assert.False(settings.HasOwned(282, !shiny));
        Assert.False(settings.HasOwned(475, !shiny));
    }

    [Fact]
    public void PreviousWurmpleBranchCanContinueWhileTheNewBaseGrowsIndependently()
    {
        var settings = Settings.NewPreview(265);
        settings.For(265).Level = 7;
        Assert.NotNull(settings.Evolve(265, 266));
        Assert.True(Hatch(settings, 265).IsRestart);
        settings.For(266).Level = 10;
        Assert.NotNull(settings.Evolve(266, 267));
        Assert.Equal(1, settings.For(265).Level);
        settings.For(265).Level = 7;
        Assert.NotNull(settings.Evolve(265, 268));
        settings.For(268).Level = 10;
        Assert.NotNull(settings.Evolve(268, 269));
        Assert.NotSame(settings.For(267), settings.For(269));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllEightEeveeBranchesRequireTheirOwnRunAndCollectionIsPerColor(bool shiny)
    {
        var settings = Settings.NewPreview(133);
        if (shiny) settings.AddOwned(133, true);
        var targets = new[] { 134, 135, 136, 196, 197, 470, 471, 700 };
        Assert.Contains(133, EvolutionData.EggPool);
        Assert.All(targets, target => Assert.DoesNotContain(target, EvolutionData.EggPool));
        settings.For(133, shiny).Level = 24;
        Assert.Empty(settings.AvailableEvolutions(133, shiny));
        Assert.False(Hatch(settings, 133, shiny).IsRestart); // Level bonus crosses threshold; no reset yet.
        Assert.Equal(25, settings.For(133, shiny).Level);
        var completed = new List<PokemonProgress>();
        foreach (var target in targets)
        {
            Assert.Equal(8 - completed.Count, settings.AvailableEvolutions(133, shiny).Count);
            Assert.NotNull(settings.Evolve(133, target, shiny));
            completed.Add(settings.For(target, shiny));
            var hatch = Hatch(settings, 133, shiny);
            if (completed.Count < targets.Length)
            {
                Assert.True(hatch.IsRestart);
                Assert.Equal(1, settings.For(133, shiny).Level);
                settings.For(133, shiny).Level = 25;
            }
            else
            {
                Assert.False(hatch.IsRestart);
                Assert.Equal(26, settings.For(133, shiny).Level);
            }
        }
        Assert.All(completed.Take(7), p => Assert.Equal(25, p.Level));
        Assert.Equal(8, completed.Distinct().Count());
        if (!settings.HasOwned(133, !shiny)) settings.AddOwned(133, !shiny);
        settings.For(133, !shiny).Level = 25;
        Assert.Equal(8, settings.AvailableEvolutions(133, !shiny).Count);
    }

    [Fact]
    public void SchemaTwoMigrationPreservesEveryIndependentRecordPreferencesSelectionAndLegacyEgg()
    {
        using var files = new SaveFiles();
        const string original = """{"SchemaVersion":2,"StarterDex":4,"SelectedDex":5,"SelectedShiny":true,"FlipHorizontal":true,"UiScale":6,"Owned":[4,5,6,134],"ShinyOwned":[4,5],"Progress":{"4":{"Level":42,"Exp":7},"5":{"Level":18,"Exp":3},"6":{"Level":55,"Exp":98},"134":{"Level":9,"Exp":33}},"ShinyProgress":{"4":{"Level":37,"Exp":91},"5":{"Level":16,"Exp":44}},"Eggs":1,"PendingEgg":{"Kind":0,"Dex":134,"IsShiny":false}}""";
        File.WriteAllText(files.Path, original);
        var settings = Settings.LoadFrom(files.Path)!;
        Assert.Equal(3, settings.SchemaVersion);
        Assert.Equal(5, settings.SelectedDex);
        Assert.True(settings.SelectedShiny);
        Assert.True(settings.FlipHorizontal);
        Assert.Equal(6, settings.UiScale);
        Assert.Equal((42, 7), (settings.For(4).Level, settings.For(4).Exp));
        Assert.Equal((18, 3), (settings.For(5).Level, settings.For(5).Exp));
        Assert.Equal((55, 98), (settings.For(6).Level, settings.For(6).Exp));
        Assert.Equal((37, 91), (settings.For(4, true).Level, settings.For(4, true).Exp));
        Assert.Equal((16, 44), (settings.For(5, true).Level, settings.For(5, true).Exp));
        Assert.NotSame(settings.For(4), settings.For(5));
        Assert.NotSame(settings.For(4, true), settings.For(5, true));
        Assert.Equal(original, File.ReadAllText(files.Path + ".schema2.bak"));
        var migrated = File.ReadAllText(files.Path);
        Settings.LoadFrom(files.Path);
        Assert.Equal(migrated, File.ReadAllText(files.Path));
        Assert.Equal(134, settings.Hatch()!.Value.Dex);
        Assert.Equal((10, 33), (settings.For(134).Level, settings.For(134).Exp));
        settings.AddOwned(133);
        settings.For(133).Level = 25;
        Assert.DoesNotContain(settings.AvailableEvolutions(133), rule => rule.TargetDex == 134);
    }

    [Fact]
    public void MainSchemaThreeGrowthIdsLoadWithoutBeingMistakenForSpeciesIds()
    {
        using var files = new SaveFiles();
        const string original = """{"SchemaVersion":3,"StarterDex":4,"SelectedDex":5,"SelectedShiny":true,"Owned":[4,5],"ShinyOwned":[4,5],"Progress":{"71":{"Level":36,"Exp":19,"CurrentDex":5,"History":[4,5],"PendingEvolution":6}},"ShinyProgress":{"88":{"Level":21,"Exp":7,"CurrentDex":5,"History":[4,5]}},"GrowthLinks":{"4":71,"5":71},"ShinyGrowthLinks":{"4":88,"5":88},"PendingEgg":{"Kind":0,"Dex":133,"IsShiny":false}}""";
        File.WriteAllText(files.Path, original);
        var settings = Settings.LoadFrom(files.Path)!;
        Assert.Same(settings.For(4), settings.For(5));
        Assert.Same(settings.For(4, true), settings.For(5, true));
        Assert.NotSame(settings.For(4), settings.For(4, true));
        Assert.Equal(6, settings.PrepareEvolution(5, random: new NoRandomAllowed()));
        Assert.Equal(original, File.ReadAllText(files.Path));
        Assert.False(File.Exists(files.Path + ".schema3.bak"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedPreparationCommitAndRestartRestoreTheirOwnAtomicBoundaries(bool shiny)
    {
        using var files = new SaveFiles();
        var settings = Settings.NewAt(280, files.Path);
        if (shiny) settings.AddOwned(280, true);
        settings.SelectedShiny = shiny;
        settings.For(280, shiny).Level = 20;
        settings.Save();
        FailSave(files, settings, () => settings.PrepareEvolution(280, shiny));
        Assert.Null(settings.For(280, shiny).PendingEvolution);
        settings.PrepareEvolution(280, shiny, 281);
        FailSave(files, settings, () => settings.CompleteEvolution(280, shiny, 281));
        Assert.Equal(281, settings.For(280, shiny).PendingEvolution);
        Assert.False(settings.HasOwned(281, shiny));
        settings.CompleteEvolution(280, shiny, 281);
        settings.For(281, shiny).Level = 30;
        Assert.NotNull(settings.Evolve(281, 282, shiny));
        var oldRun = settings.For(282, shiny);
        settings.PendingEgg = new(EggKind.Common, 280, shiny);
        settings.Eggs = 1;
        settings.Save();
        FailSave(files, settings, () => settings.Hatch());
        Assert.Same(oldRun, settings.For(280, shiny));
        Assert.True(settings.Hatch()!.Value.IsRestart);
        settings.For(280, shiny).Level = 20;
        settings.PrepareEvolution(280, shiny, 281);
        // The intermediate already belongs to the earlier run: rollback must preserve its link/object.
        FailSave(files, settings, () => settings.CompleteEvolution(280, shiny, 281));
        Assert.Same(oldRun, settings.For(281, shiny));
    }

    [Fact]
    public void BrokenSchemaThreeLinksAreRejectedWithoutReplacingTheSave()
    {
        using var files = new SaveFiles();
        var settings = Settings.NewAt(4, files.Path);
        settings.GrowthLinks[4] = int.MaxValue;
        var original = JsonSerializer.Serialize(settings);
        File.WriteAllText(files.Path, original);
        Assert.Throws<InvalidDataException>(() => Settings.LoadFrom(files.Path));
        Assert.Equal(original, File.ReadAllText(files.Path));
        Assert.False(File.Exists(files.Path + ".schema3.bak"));
    }

    [Theory]
    [InlineData("Progress")]
    [InlineData("ShinyProgress")]
    [InlineData("GrowthLinks")]
    [InlineData("ShinyGrowthLinks")]
    [InlineData("Owned")]
    [InlineData("ShinyOwned")]
    public void MissingOrNullSchemaThreeCollectionsCannotSilentlyResetExistingGrowth(string field)
    {
        using var files = new SaveFiles();
        var settings = Settings.NewAt(4, files.Path);
        settings.AddOwned(4, true);
        settings.For(4).Level = 42;
        settings.For(4, true).Level = 31;
        foreach (var nullValue in new[] { false, true })
        {
            var json = JsonNode.Parse(JsonSerializer.Serialize(settings))!.AsObject();
            if (nullValue) json[field] = null;
            else json.Remove(field);
            var original = json.ToJsonString();
            File.WriteAllText(files.Path, original);
            Assert.Throws<InvalidDataException>(() => Settings.LoadFrom(files.Path));
            Assert.Equal(original, File.ReadAllText(files.Path));
            Assert.False(File.Exists(files.Path + ".schema3.bak"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOneSchemaThreeOwnershipLinkCannotCreateAFreshLevelOneRecord(bool shiny)
    {
        using var files = new SaveFiles();
        var settings = Settings.NewAt(4, files.Path);
        settings.AddOwned(4, true);
        settings.For(4, shiny).Level = 36;
        Assert.NotNull(settings.Evolve(4, 5, shiny));
        (shiny ? settings.ShinyGrowthLinks : settings.GrowthLinks).Remove(4);
        var original = JsonSerializer.Serialize(settings);
        File.WriteAllText(files.Path, original);
        Assert.Throws<InvalidDataException>(() => Settings.LoadFrom(files.Path));
        Assert.Equal(original, File.ReadAllText(files.Path));
    }

    [Fact]
    public void DebugUnlockAndOtherColorsDoNotCreateAnEvolutionOrBoostFirstAcquisition()
    {
        var settings = Settings.NewPreview(4);
        settings.UnlockAll = true;
        settings.For(133).Level = 100;
        Assert.Empty(settings.EvolutionOptions(133));
        settings.AddOwned(133);
        Assert.Equal(1, settings.For(133).Level);
        settings.For(133).Level = 25;
        Assert.NotNull(settings.Evolve(133, 134));
        settings.AddOwned(133, true);
        Assert.Equal(1, settings.For(133, true).Level);
        Assert.Empty(settings.AvailableEvolutions(133, true));
    }

    [Fact]
    public void BundledDetailsCoverSpeciesAndRegionalTypesWithoutRuntimeNetwork()
    {
        foreach (var dex in Enumerable.Range(1, 1025).Concat(EvolutionData.Forms.Select(form => form.Id)))
        {
            var details = PokemonDetails.For(dex);
            Assert.Equal(dex, details.Dex);
            Assert.False(string.IsNullOrWhiteSpace(details.Description));
            Assert.InRange(details.Types.Length, 1, 2);
        }
        Assert.Equal(new[] { "풀", "독" }, PokemonDetails.For(1).Types);
        Assert.Equal(new[] { "독", "땅" }, PokemonDetails.For(8194).Types);
        Assert.Equal(new[] { "강철" }, PokemonDetails.For(4052).Types);
        Assert.StartsWith("분류: 지배포켓몬", PokemonDetails.For(1025).Description);
        Assert.Throws<ArgumentOutOfRangeException>(() => PokemonDetails.For(1026));
    }

    private static HatchResult Hatch(Settings settings, int dex, bool shiny = false)
    {
        settings.PendingEgg = new(EggKind.Common, dex, shiny);
        settings.Eggs = 1;
        return settings.Hatch()!.Value;
    }

    private static void FailSave(SaveFiles files, Settings settings, Action operation)
    {
        var before = JsonSerializer.Serialize(settings);
        var disk = File.ReadAllBytes(files.Path);
        var preserved = files.Path + ".original";
        File.Move(files.Path, preserved);
        Directory.CreateDirectory(files.Path); // Portable failure at atomic promotion on Windows and POSIX.
        try
        {
            Assert.ThrowsAny<IOException>(operation);
            Assert.Equal(before, JsonSerializer.Serialize(settings));
            Assert.Equal(disk, File.ReadAllBytes(preserved));
            Assert.Empty(Directory.EnumerateFiles(files.Directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(files.Path);
            File.Move(preserved, files.Path);
        }
    }

    private sealed class IndexRandom(int index) : Random
    {
        public override int Next(int maxValue)
        {
            Assert.InRange(index, 0, maxValue - 1);
            return index;
        }
    }

    private sealed class NoRandomAllowed : Random
    {
        public override int Next(int maxValue) => throw new InvalidOperationException("A saved target must not be rerolled.");
    }

    private sealed class SaveFiles : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PokeDesk-evolution-tests-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Directory, "settings.json");
        public SaveFiles() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
