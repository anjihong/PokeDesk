using System.Text.Json;
using Xunit;

namespace DeskPokemon.Tests;

public class EvolutionTests
{
    [Theory]
    [InlineData(1, 2, 16)]
    [InlineData(2, 3, 32)]
    [InlineData(4, 5, 16)]
    [InlineData(5, 6, 36)]
    [InlineData(129, 130, 20)]
    [InlineData(147, 148, 30)]
    [InlineData(148, 149, 55)]
    [InlineData(723, 724, 34)] // Ordinary Decidueye, not the Hisuian form's level36.
    [InlineData(997, 998, 54)]
    [InlineData(172, 25, 25)] // Friendship: first-step fallback.
    [InlineData(25, 26, 40)] // Stone after a baby stage: second-step fallback.
    [InlineData(64, 65, 40)] // Trade: second-step fallback.
    [InlineData(999, 1000, 25)] // Coins: first-step fallback.
    public void DirectEvolutionUsesCanonicalOrStageFallbackLevel(int from, int target, int level)
    {
        var rule = Assert.Single(EvolutionRules.OptionsFor(from), option => option.TargetDex == target);
        Assert.Equal(level, rule.RequiredLevel);
        var settings = Settings.NewPreview(4);
        settings.Owned.Add(from);
        settings.For(from).Level = level - 1;
        Assert.DoesNotContain(settings.AvailableEvolutions(from), option => option.TargetDex == target);
        Assert.Null(settings.Evolve(from, target));
        settings.For(from).Level++;
        Assert.Contains(settings.AvailableEvolutions(from), option => option.TargetDex == target);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EvolutionPersistsOneStepAndCopiesGrowthOnlyWithinTheSelectedColor(bool shiny)
    {
        using var files = new SaveFiles();
        var settings = Settings.NewAt(4, files.Path);
        settings.Owned.Add(4);
        settings.ShinyOwned.Add(4);
        settings.SelectedShiny = shiny;
        settings.For(4, shiny).Level = 16;
        settings.For(4, shiny).Exp = 91;
        settings.For(4, !shiny).Level = 50;
        settings.For(4, !shiny).Exp = 12;
        var pending = settings.PendingEgg;

        var result = settings.Evolve(4, 5, shiny);

        Assert.Equal(new EvolutionResult(4, 5, shiny, 16, 91), result);
        Assert.Equal(5, settings.SelectedDex);
        Assert.Equal(shiny, settings.SelectedShiny);
        Assert.True(settings.IsOwned(4, shiny));
        Assert.True(settings.IsOwned(5, shiny));
        Assert.False(settings.IsOwned(5, !shiny));
        Assert.False(settings.IsOwned(6, shiny));
        Assert.Equal((16, 91), (settings.For(4, shiny).Level, settings.For(4, shiny).Exp));
        Assert.Equal((16, 91), (settings.For(5, shiny).Level, settings.For(5, shiny).Exp));
        Assert.Equal((50, 12), (settings.For(4, !shiny).Level, settings.For(4, !shiny).Exp));
        var loaded = Settings.LoadFrom(files.Path)!;
        Assert.Equal(5, loaded.SelectedDex);
        Assert.Equal(shiny, loaded.SelectedShiny);
        Assert.Equal((16, 91), (loaded.For(5, shiny).Level, loaded.For(5, shiny).Exp));
        Assert.Equal(pending, loaded.PendingEgg);
    }

    [Fact]
    public void AnOldFormCanEarnTheNextStageWithoutSkippingTheOwnedIntermediate()
    {
        var settings = Settings.NewPreview(4);
        settings.For(4).Level = 16;
        Assert.NotNull(settings.Evolve(4, 5));
        settings.SelectedDex = 4;
        settings.For(4).Level = 36;
        settings.For(4).Exp = 17;

        Assert.Empty(settings.AvailableEvolutions(4));
        Assert.Null(settings.Evolve(4, 5));
        Assert.Null(settings.Evolve(4, 6));
        Assert.False(settings.IsOwned(6));
        Assert.Equal(16, settings.For(5).Level);
        Assert.Equal(36, settings.EffectiveEvolutionLevel(5));
        Assert.Equal(new EvolutionOption(6, 36), Assert.Single(settings.AvailableEvolutions(5)));

        settings.SelectedDex = 5;
        Assert.Equal(new EvolutionResult(5, 6, false, 36, 17), settings.Evolve(5, 6));
        Assert.Equal(6, settings.SelectedDex);
        Assert.Equal((36, 17), (settings.For(6).Level, settings.For(6).Exp));
        Assert.Equal((16, 0), (settings.For(5).Level, settings.For(5).Exp));
        Assert.True(settings.IsOwned(4));
        Assert.True(settings.IsOwned(5));
        Assert.Empty(settings.AvailableEvolutions(5));
    }

    [Fact]
    public void SameColorAncestorsQualifyButSiblingsOtherColorsAndDebugUnlockDoNot()
    {
        var settings = Settings.NewPreview(4);
        settings.For(4).Level = 100;
        settings.ShinyOwned.Add(5);
        settings.For(5, true).Level = 1;
        Assert.Equal(1, settings.EffectiveEvolutionLevel(5, true));
        Assert.False(settings.CanEvolve(5, true));
        Assert.False(settings.CanEvolve(5)); // Unowned intermediate cannot be skipped.
        settings.UnlockAll = true;
        Assert.False(settings.CanEvolve(5));

        settings.Owned.Add(265);
        settings.Owned.Add(266);
        settings.Owned.Add(268);
        settings.For(266).Level = 100;
        Assert.Equal(1, settings.EffectiveEvolutionLevel(268)); // Branch siblings do not share growth.
        Assert.False(settings.CanEvolve(268));
    }

    [Fact]
    public void BranchesAreExplicitUnownedOptionsAndCannotBeRepeatedForExtraLevels()
    {
        var settings = Settings.NewPreview(4);
        settings.Owned.Add(236);
        settings.For(236).Level = 20;
        Assert.Equal(new[] { 106, 107, 237 }, settings.AvailableEvolutions(236).Select(option => option.TargetDex));
        Assert.NotNull(settings.Evolve(236, 107));
        Assert.Equal(4, settings.SelectedDex); // Evolving another form cannot steal selection.
        Assert.Equal(new[] { 106, 237 }, settings.AvailableEvolutions(236).Select(option => option.TargetDex));
        Assert.Null(settings.Evolve(236, 107));
        Assert.Equal(20, settings.For(107).Level);
    }

    [Fact]
    public void EqualLevelUsesGreaterExperienceAndStrongerOrphanGrowthIsPreserved()
    {
        var settings = Settings.NewPreview(4);
        settings.Owned.Add(5);
        settings.For(4).Level = settings.For(5).Level = 36;
        settings.For(4).Exp = 98;
        settings.For(5).Exp = 45;
        Assert.Equal(98, settings.Evolve(5, 6)!.Value.Exp);

        var recovered = Settings.NewPreview(4);
        recovered.For(4).Level = 16;
        recovered.For(5).Level = 20; // Old orphan data with no ownership must not lose growth.
        recovered.For(5).Exp = 56;
        Assert.Equal(new EvolutionResult(4, 5, false, 20, 56), recovered.Evolve(4, 5));
    }

    [Fact]
    public void EeveeAndAllEightEvolutionsAreEggCandidatesInsteadOfEvolutionTargets()
    {
        var eeveeFamily = new[] { 133, 134, 135, 136, 196, 197, 470, 471, 700 };
        Assert.Empty(EvolutionRules.OptionsFor(133));
        foreach (var dex in eeveeFamily)
        {
            Assert.Contains(dex, BaseSpecies.Dex);
            var pool = BaseSpecies.Dex.Where(d => !PokemonRarity.IsSpecial(d)).ToArray();
            var reward = EggHatcher.Create(EggKind.Common, rng: new IndexRandom(Array.IndexOf(pool, dex)));
            Assert.Equal(dex, reward.Dex);
        }
        using var files = new SaveFiles();
        var settings = Settings.NewAt(133, files.Path);
        settings.PendingEgg = new PendingEgg(EggKind.Common, 700, false);
        settings.Save();
        Assert.Equal(settings.PendingEgg, Settings.LoadFrom(files.Path)!.PendingEgg);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FailedEvolutionSaveRestoresOwnershipSelectionAndExistingProgress(bool shiny, bool orphan)
    {
        using var files = new SaveFiles();
        var settings = Settings.NewAt(4, files.Path);
        if (shiny) settings.ShinyOwned.Add(4);
        settings.SelectedShiny = shiny;
        settings.For(4, shiny).Level = 16;
        settings.For(4, shiny).Exp = 44;
        PokemonProgress? previous = null;
        if (orphan)
        {
            previous = settings.For(5, shiny);
            previous.Level = 9;
            previous.Exp = 33;
        }
        settings.Save();
        var before = JsonSerializer.Serialize(settings);
        var bytes = File.ReadAllBytes(files.Path);
        var preserved = files.Path + ".original";
        File.Move(files.Path, preserved);
        Directory.CreateDirectory(files.Path); // Portable failure at atomic promotion, after temp write.
        try
        {
            Assert.ThrowsAny<IOException>(() => settings.Evolve(4, 5, shiny));
            Assert.Equal(before, JsonSerializer.Serialize(settings));
            Assert.DoesNotContain(5, shiny ? settings.ShinyOwned : settings.Owned);
            if (orphan) Assert.Same(previous, settings.For(5, shiny));
            Assert.Equal(bytes, File.ReadAllBytes(preserved));
            Assert.Empty(Directory.EnumerateFiles(files.Directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(files.Path);
            File.Move(preserved, files.Path);
        }
        Assert.Equal(4, Settings.LoadFrom(files.Path)!.SelectedDex);
    }

    [Fact]
    public void BundledDetailsCoverEverySpeciesWithoutRuntimeNetworkRequests()
    {
        for (var dex = 1; dex <= 1025; dex++)
        {
            var details = PokemonDetails.For(dex);
            Assert.Equal(dex, details.Dex);
            Assert.False(string.IsNullOrWhiteSpace(details.Description));
            Assert.InRange(details.Types.Length, 1, 2);
            Assert.All(details.Types, type => Assert.False(string.IsNullOrWhiteSpace(type)));
        }
        Assert.Equal(new[] { "풀", "독" }, PokemonDetails.For(1).Types);
        Assert.Equal(new[] { "독", "고스트" }, PokemonDetails.For(1025).Types);
        Assert.StartsWith("분류: 지배포켓몬", PokemonDetails.For(1025).Description);
        Assert.Throws<ArgumentOutOfRangeException>(() => PokemonDetails.For(1026));
    }

    private sealed class IndexRandom(int index) : Random
    {
        public override int Next(int maxValue) => index;
        public override double NextDouble() => .99;
    }

    private sealed class SaveFiles : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PokeDesk-evolution-tests-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Directory, "settings.json");
        public SaveFiles() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
