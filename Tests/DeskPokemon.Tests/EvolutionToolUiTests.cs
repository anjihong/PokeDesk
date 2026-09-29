#if DEBUG
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace DeskPokemon.Tests;

public partial class UiTests
{
    [AvaloniaFact]
    public void EvolutionTestSaveIsSeparateAndInvalidTestDataIsPreserved()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PokeDesk-test-mode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var normalPath = Path.Combine(directory, "settings.json");
            var testPath = Path.Combine(directory, "evolution-test.json");
            Settings.NewAt(7, normalPath);
            var normal = File.ReadAllBytes(normalPath);
            var settings = App.LoadStartupSettings(true, testPath)!;
            Assert.Equal(4, settings.StarterDex);
            settings.GrantTestEgg(133, true);
            var reloaded = App.LoadStartupSettings(true, testPath)!;
            Assert.Equal(133, reloaded.PendingEgg!.Dex);
            Assert.True(reloaded.PendingEgg.IsShiny);
            Assert.Equal(normal, File.ReadAllBytes(normalPath));

            File.WriteAllText(testPath, "invalid test save");
            Assert.Throws<InvalidDataException>(() => App.LoadStartupSettings(true, testPath));
            Assert.Equal("invalid test save", File.ReadAllText(testPath));
            Assert.Equal(normal, File.ReadAllBytes(normalPath));
            Assert.NotEqual(AppPaths.SettingsFile, App.EvolutionTestSavePath);
            Assert.Equal(AppPaths.DataDirectory, Path.GetDirectoryName(App.EvolutionTestSavePath));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [AvaloniaFact]
    public void EvolutionToolFiltersSpeciesAndPreparesTheRequestedColorWithoutGrantingOwnership()
    {
        var settings = Settings.NewPreview(4);
        var window = NewEvolutionToolWindow(settings);
        Window? panel = null;
        try
        {
            window.Show();
            Invoke(window, "ShowEvolutionTestPanel");
            panel = Field<Window>(window, "_testPanel");
            Dispatcher.UIThread.RunJobs();
            panel.UpdateLayout();
            Assert.Contains("별도 세이브", panel.Title);
            Assert.Equal(SystemDecorations.None, panel.SystemDecorations);
            Assert.Contains("Galmuri11", panel.FontFamily.Name);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "EvolutionTestBanner");
            var category = ToolControl<ComboBox>(panel, "TestCategory");
            var search = ToolControl<TextBox>(panel, "TestSearch");
            var species = ToolControl<ComboBox>(panel, "TestSpecies");
            var prepare = ToolControl<Button>(panel, "TestPrepareEgg");
            Assert.Contains(species.Items, item => TestOptionDex(item!) == 133);
            Assert.DoesNotContain(species.Items, item => TestOptionDex(item!) == 4);

            category.SelectedIndex = 1;
            Assert.NotEmpty(species.Items);
            Assert.All(species.Items, item => Assert.NotNull(EvolutionData.Form(TestOptionDex(item!))));
            category.SelectedIndex = 2;
            search.Text = "파이리";
            Dispatcher.UIThread.RunJobs();
            Assert.Single(species.Items);
            Assert.Equal(4, TestOptionDex(species.SelectedItem!));
            ToolControl<CheckBox>(panel, "TestShiny").IsChecked = true;
            prepare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(4, settings.PendingEgg!.Dex);
            Assert.True(settings.PendingEgg.IsShiny);
            Assert.Equal(1, settings.Eggs);
            Assert.Empty(settings.ShinyOwned);

            search.Text = "없는 포켓몬 이름";
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(species.Items);
            Assert.False(prepare.IsEnabled);
            Invoke(window, "ShowEvolutionTestPanel");
            Assert.Same(panel, Field<Window>(window, "_testPanel"));
            window.Close();
            Assert.False(panel.IsVisible);
            Assert.Null(Field<Window?>(window, "_testPanel"));
        }
        finally { panel?.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task EvolutionToolJumpsOnlyTheSelectedColorAndCanSelectItsPreviousForm()
    {
        using var assets = new UiAssets();
        var settings = Settings.NewPreview(4);
        settings.For(4).Level = 3;
        settings.For(4).Exp = 5;
        settings.AddOwned(4, true);
        settings.SelectedShiny = true;
        settings.For(4, true).Exp = 7;
        var window = NewEvolutionToolWindow(settings);
        Window? panel = null;
        try
        {
            window.Show();
            Invoke(window, "BuildGenTabs");
            Assert.True(await InvokeAsync<bool>(window, "LoadPokemonAsync", 4, true));
            Invoke(window, "ShowEvolutionTestPanel");
            panel = Field<Window>(window, "_testPanel");
            Dispatcher.UIThread.RunJobs();
            panel.UpdateLayout();
            var jump = ToolControl<Button>(panel, "TestJumpLevel");
            Assert.True(jump.IsEnabled);
            jump.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await EventuallyAsync(window, () => settings.SelectedDex == 5 && !Field<bool>(window, "_evolving"));
            Assert.True(settings.SelectedShiny);
            Assert.True(settings.HasOwned(5, true));
            Assert.False(settings.HasOwned(5));
            Assert.Equal(3, settings.For(4).Level);
            Assert.Equal(5, settings.For(4).Exp);
            Assert.Equal(16, settings.For(5, true).Level);
            Assert.Equal(7, settings.For(5, true).Exp);

            var previous = ToolControl<Button>(panel, "TestPreviousForm");
            await EventuallyAsync(window, () => previous.IsEnabled);
            previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await EventuallyAsync(window, () => settings.SelectedDex == 4 && !Field<bool>(window, "_dexLoading"));
            Assert.True(settings.SelectedShiny);
            Assert.Equal(5, settings.For(4, true).CurrentDex);
            Assert.Same(settings.For(4, true), settings.For(5, true));
            ToolControl<Button>(panel, "TestRepeatEgg").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(4, settings.PendingEgg!.Dex);
            Assert.True(settings.PendingEgg.IsShiny);
        }
        finally { panel?.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public void EvolutionTestModeDoesNotConstructAGlobalInputHook()
    {
        var window = NewEvolutionToolWindow(Settings.NewPreview(4), startServices: true);
        try
        {
            Assert.Null(Field<InputHook?>(window, "_hook"));
            Assert.Equal(0, window.Opacity);
            Assert.Contains(window.ContextMenu!.Items.OfType<MenuItem>(), item => Equals(item.Header, "일반 모드로 돌아가기"));
            Assert.Contains(window.ContextMenu.Items.OfType<MenuItem>(), item => Equals(item.Header, "진화 테스트 세이브 초기화"));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalClicksAndKeysGrantExperienceOnlyInNormalMode(bool testMode)
    {
        var settings = Settings.NewPreview(4);
        var window = testMode ? NewEvolutionToolWindow(settings)
            : new MainWindow(settings, false, new FakeStartupRegistration());
        // Enable local input without opening native hooks, loading remote art, or writing a real save.
        typeof(MainWindow).GetField("_startServices", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var menu = window.FindControl<StackPanel>("MenuTabs")!.Children.OfType<ToggleButton>().First();
            var point = menu.TranslatePoint(new Point(menu.Bounds.Width / 2, menu.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            menu.Focus();
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None); // Holding a key must not count twice in normal fallback mode.
            window.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, settings.For(4).Level);
            Assert.Equal(testMode ? 0 : 3, settings.For(4).Exp);
        }
        finally { window.Close(); }
    }

    private static MainWindow NewEvolutionToolWindow(Settings settings, bool startServices = false)
    {
        var previous = App.EvolutionTestMode;
        App.EvolutionTestMode = true;
        try { return new MainWindow(settings, startServices, new FakeStartupRegistration()); }
        finally { App.EvolutionTestMode = previous; }
    }

    private static T ToolControl<T>(Window panel, string name) where T : Control =>
        panel.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static int TestOptionDex(object option) => (int)option.GetType().GetProperty("Dex")!.GetValue(option)!;
}
#endif
