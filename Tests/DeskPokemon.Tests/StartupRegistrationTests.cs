using System.Xml.Linq;
using DeskPokemon.Platform;
using Xunit;

namespace DeskPokemon.Tests;

public class StartupRegistrationTests
{
    [Fact]
    public void WindowsQueryDoesNotMutateAndEnableQuotesTheExactExecutable()
    {
        var store = new FakeRunStore();
        const string path = @"C:\Apps & Games\포켓 데스크\DeskPokemon.exe";
        var startup = new WindowsStartupRegistration(store, () => path, _ => true);

        Assert.False(startup.Query().IsEnabled);
        Assert.Equal(0, store.Writes);
        Assert.Equal(0, store.Deletes);
        var enabled = startup.SetEnabled(true);
        Assert.True(enabled.IsSupported);
        Assert.True(enabled.IsEnabled);
        Assert.Null(enabled.Error);
        Assert.Equal('"' + path + '"', store.Command);
        Assert.Equal(1, store.Writes);
        Assert.False(startup.SetEnabled(false).IsEnabled);
        Assert.Null(store.Command);
    }

    [Theory]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe")]
    [InlineData(@"C:\App\DeskPokemon.dll")]
    [InlineData("DeskPokemon.exe")]
    [InlineData("C:\\bad\"path\\DeskPokemon.exe")]
    public void WindowsRejectsUnusableCommandsWithoutChangingExistingRegistration(string path)
    {
        var store = new FakeRunStore { Command = @"""C:\Installed\DeskPokemon.exe""" };
        var startup = new WindowsStartupRegistration(store, () => path, _ => true);

        var result = startup.SetEnabled(true);

        Assert.NotNull(result.Error);
        Assert.True(result.IsEnabled);
        Assert.Equal(0, store.Writes);
        Assert.Null(startup.SetEnabled(false).Error); // Disabling does not need a usable current host.
        Assert.Null(store.Command);
    }

    [Fact]
    public void WindowsReportsRegistryFailuresAndKeepsTheObservedState()
    {
        var store = new FakeRunStore { Command = "existing", FailWrite = true };
        var startup = new WindowsStartupRegistration(store, () => @"C:\App\DeskPokemon.exe", _ => true);

        var result = startup.SetEnabled(true);

        Assert.True(result.IsEnabled);
        Assert.Contains("자동 시작 설정을 변경하지 못했습니다", result.Error);
        Assert.Equal("existing", store.Command);
    }

    [Fact]
    public void WindowsRejectsCommandsOverTheRunKeyLimit()
    {
        var store = new FakeRunStore();
        var startup = new WindowsStartupRegistration(store, () => @"C:\" + new string('a', 260) + ".exe", _ => true);
        Assert.NotNull(startup.SetEnabled(true).Error);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public void MacRegistrationPreservesBundleExecutableAsOneXmlArgument()
    {
        using var directory = new TemporaryDirectory();
        var launchAgents = Path.Combine(directory.Path, "Library", "LaunchAgents");
        const string executable = "/Applications/Poké Desk & \"friends\".app/Contents/MacOS/DeskPokemon";
        var startup = new MacStartupRegistration(launchAgents, () => executable, _ => true);
        Assert.False(startup.Query().IsEnabled);
        Assert.False(Directory.Exists(launchAgents));
        Assert.Null(startup.SetEnabled(false).Error);
        Assert.False(Directory.Exists(launchAgents));

        var result = startup.SetEnabled(true);

        Assert.Null(result.Error);
        Assert.True(result.IsEnabled);
        var document = XDocument.Load(startup.RegistrationPath);
        var dictionary = document.Root!.Element("dict")!;
        XElement Value(string key) => dictionary.Elements("key").Single(element => element.Value == key).ElementsAfterSelf().First();
        Assert.Equal(executable, Assert.Single(Value("ProgramArguments").Elements("string")).Value);
        Assert.Equal("true", Value("RunAtLoad").Name.LocalName);
        Assert.Equal("Aqua", Value("LimitLoadToSessionType").Value);
        Assert.DoesNotContain(dictionary.Elements("key"), key => key.Value == "KeepAlive");
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(startup.RegistrationPath));
        Assert.Empty(Directory.GetFiles(launchAgents, "*.tmp"));
        Assert.Null(startup.SetEnabled(false).Error);
        Assert.False(File.Exists(startup.RegistrationPath));
    }

    [Fact]
    public void MacUpdatesOnlyItsOwnFileAndHandlesMalformedRegistration()
    {
        using var directory = new TemporaryDirectory();
        var other = Path.Combine(directory.Path, "other.plist");
        File.WriteAllText(other, "leave me alone");
        var startup = new MacStartupRegistration(directory.Path, () => "/Applications/DeskPokemon", _ => true);
        File.WriteAllText(startup.RegistrationPath, "broken XML");
        Assert.NotNull(startup.Query().Error);
        Assert.Null(startup.SetEnabled(true).Error);
        Assert.True(startup.Query().IsEnabled);
        Assert.Null(startup.SetEnabled(false).Error);
        Assert.Equal("leave me alone", File.ReadAllText(other));
    }

    [Fact]
    public void MacDevelopmentHostAndWriteFailuresDoNotCreateLoginEntries()
    {
        using var directory = new TemporaryDirectory();
        var launchAgents = Path.Combine(directory.Path, "LaunchAgents");
        var startup = new MacStartupRegistration(launchAgents, () => "/usr/local/share/dotnet/dotnet", _ => true);
        Assert.Contains("dotnet", startup.SetEnabled(true).Error);
        Assert.False(Directory.Exists(launchAgents));
        File.WriteAllText(launchAgents, "not a directory");
        startup = new MacStartupRegistration(launchAgents, () => "/Applications/DeskPokemon", _ => true);
        Assert.NotNull(startup.SetEnabled(true).Error);
        Assert.Equal("not a directory", File.ReadAllText(launchAgents));
    }

    [Fact]
    public void MissingExecutableIsReportedBeforeAnyWrite()
    {
        var store = new FakeRunStore();
        var startup = new WindowsStartupRegistration(store, () => @"C:\Missing\DeskPokemon.exe", _ => false);
        Assert.Contains("찾을 수 없습니다", startup.SetEnabled(true).Error);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public void UnsupportedPlatformDoesNotClaimToRegister()
    {
        IStartupRegistration startup = new UnsupportedStartupRegistration();
        Assert.False(startup.Query().IsSupported);
        Assert.False(startup.SetEnabled(true).IsEnabled);
        Assert.NotNull(startup.SetEnabled(true).Error);
    }

    private sealed class FakeRunStore : IWindowsStartupStore
    {
        public string? Command;
        public int Writes;
        public int Deletes;
        public bool FailWrite;
        public string? Read() => Command;
        public void Write(string command)
        {
            if (FailWrite) throw new UnauthorizedAccessException("Synthetic registry denial");
            Writes++;
            Command = command;
        }
        public void Delete() { Deletes++; Command = null; }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PokeDeskStartupTests", Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
