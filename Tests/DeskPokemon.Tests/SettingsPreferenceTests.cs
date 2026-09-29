using Xunit;

namespace DeskPokemon.Tests;

public class SettingsPreferenceTests
{
    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("123")]
    public void InvalidSaveCannotBeMistakenForANewGameOrOverwritten(string original)
    {
        using var file = new SaveFile();
        File.WriteAllText(file.Path, original);
        Assert.Throws<InvalidDataException>(() => Settings.LoadFrom(file.Path));
        Assert.Equal(original, File.ReadAllText(file.Path));
        Assert.Equal(new[] { file.Path }, Directory.GetFiles(file.Directory));
    }

    [Fact]
    public void MissingSaveStillStartsANewGame()
    {
        using var file = new SaveFile();
        Assert.Null(Settings.LoadFrom(file.Path));
        Assert.Empty(Directory.GetFiles(file.Directory));
    }

    [Fact]
    public void SchemaTwoWithoutOptionalPreferencesKeepsSelectionGrowthAndCommittedEgg()
    {
        using var file = new SaveFile();
        const string original = """{"SchemaVersion":2,"StarterDex":4,"SelectedDex":7,"SelectedShiny":true,"Owned":[4],"ShinyOwned":[7],"ShinyProgress":{"7":{"Level":12,"Exp":87}},"PendingEgg":{"Kind":0,"Dex":172,"IsShiny":true},"Eggs":1,"EggSeconds":56}""";
        File.WriteAllText(file.Path, original);
        var settings = Settings.LoadFrom(file.Path)!;
        Assert.False(settings.FlipHorizontal);
        Assert.Equal(2, settings.UiScale);
        Assert.Equal(3, settings.SchemaVersion);
        Assert.Equal(7, settings.SelectedDex);
        Assert.True(settings.SelectedShiny);
        Assert.Equal((12, 87), (settings.For(7, true).Level, settings.For(7, true).Exp));
        Assert.Equal(new PendingEgg(EggKind.Common, 172, true), settings.PendingEgg);
        Assert.Equal(1, settings.Eggs);
        Assert.Equal(56, settings.EggSeconds);
        Assert.Equal(original, File.ReadAllText(file.Path + ".schema2.bak"));
        var migrated = File.ReadAllText(file.Path);
        Settings.LoadFrom(file.Path);
        Assert.Equal(migrated, File.ReadAllText(file.Path));
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(4, 4)]
    [InlineData(6, 6)]
    [InlineData(8, 8)]
    [InlineData(0, 2)]
    [InlineData(3, 2)]
    [InlineData(-2, 2)]
    [InlineData(99, 2)]
    public void PreferencesPersistAndUnsupportedScaleRecoversToTwo(int scale, int expected)
    {
        using var file = new SaveFile();
        var settings = Settings.NewAt(4, file.Path);
        settings.FlipHorizontal = true;
        settings.UiScale = scale;
        settings.Save();
        var loaded = Settings.LoadFrom(file.Path)!;
        Assert.True(loaded.FlipHorizontal);
        Assert.Equal(expected, loaded.UiScale);
        Assert.Equal(expected, Settings.LoadFrom(file.Path)!.UiScale);
    }

    private sealed class SaveFile : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PokeDesk-preference-tests-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Directory, "settings.json");
        public SaveFile() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
