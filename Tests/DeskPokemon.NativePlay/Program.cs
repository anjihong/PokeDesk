using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using DeskPokemon;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Native play uses macOS CGEvent input.");
        AppBuilder.Configure<PlayApp>().UsePlatformDetect()
        .With(new MacOSPlatformOptions { ShowInDock = false })
        .With(new FontManagerOptions { DefaultFamilyName = "avares://DeskPokemon/Assets/Fonts#Galmuri11" })
        .LogToTrace().StartWithClassicDesktopLifetime(args);
    }
}

public sealed class FakeStartup : IStartupRegistration
{
    public bool Enabled;
    public int Writes;
    public StartupRegistrationResult Query() => new(true, Enabled);
    public StartupRegistrationResult SetEnabled(bool enabled) { Writes++; Enabled = enabled; return Query(); }
}

public sealed class PlayApp : App
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string Output = Path.Combine(Environment.CurrentDirectory, "artifacts", "native-play");
    private static object? Invoke(MainWindow window, string name, params object[] args) =>
        typeof(MainWindow).GetMethod(name, Private | BindingFlags.DeclaredOnly)!.Invoke(window, args);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, Private)!.GetValue(window)!;
    private static T Control<T>(MainWindow window, string name) where T : Control => window.FindControl<T>(name)!;
    private static ToggleButton MenuTab(MainWindow window, string tag) =>
        Control<StackPanel>(window, "MenuTabs").Children.OfType<ToggleButton>().Single(tab => Equals(tab.Tag, tag));
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
    private static async Task Wait(Func<bool> condition, string message, int timeout = 20000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(timeout);
        while (!condition())
        {
            if (DateTime.UtcNow >= end) throw new TimeoutException(message);
            await Task.Delay(40);
        }
    }
    private static string? SaveHash() => File.Exists(AppPaths.SettingsFile)
        ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(AppPaths.SettingsFile))) : null;

    public override void OnFrameworkInitializationCompleted()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var referenceMode = desktop.Args?.Contains("--reference", StringComparer.Ordinal) == true;
        var originalSave = SaveHash();
        var settings = (Settings)typeof(Settings).GetMethod("NewPreview", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [4])!;
        settings.For(4).Exp = 0;
        if (referenceMode)
        {
            settings.PendingEgg = new(EggKind.Common, 4, false);
            settings.Eggs = 0;
            settings.EggSeconds = 0;
        }
        else
        {
            settings.AddOwned(4, true);
            settings.PendingEgg = new(EggKind.Rare, 7, true);
            settings.Eggs = 1;
        }
        var startup = new FakeStartup();
        var window = (MainWindow)Activator.CreateInstance(typeof(MainWindow), Private, null, [settings, true, startup], null)!;
        desktop.MainWindow = window;
        window.Opened += async (_, _) =>
        {
            try
            {
                await Wait(() => Field<bool>(window, "_placed") && Control<Image>(window, "Sprite").Source != null, "native startup", 60000);
                await Task.Delay(400);
                window.Position = new PixelPoint(200, 150);
                window.Activate();
                Check(startup.Writes == 0, "opening preferences never enables OS startup");
                Console.WriteLine($"NATIVE renderScale={window.RenderScaling} desktopScale={window.DesktopScaling} size={window.Bounds.Size}");
                var hook = Field<InputHook?>(window, "_hook");
                Console.WriteLine($"NATIVE inputActive={hook?.IsActive} inputStatus={hook?.Status}");
                if (referenceMode)
                {
                    await CaptureReferenceViews(window, settings, startup);
                    Console.WriteLine("NATIVE_REFERENCE_PASS");
                    return;
                }
                Capture(window, "01-collapsed");
                CheckPetSizeAndGrounding(window, "Charmander", 76);
                var track = Control<Border>(window, "ExpTrack");
                for (var attempt = 0; attempt < 5 && !ToolTip.GetIsOpen(track); attempt++)
                {
                    await Move(Control<TextBlock>(window, "PetName"));
                    await Move(track);
                }
                await Wait(() => ToolTip.GetIsOpen(track), "experience hover");
                var tip = (ToolTip)ToolTip.GetTip(track)!;
                var tipText = ((Decorator)tip.Content!).Child as TextBlock;
                var hoverProgress = settings.For(settings.SelectedDex, settings.SelectedShiny);
                var expectedTip = $"현재 경험치: {hoverProgress.Exp:N0}\n필요 경험치: {Settings.ExpToNext(hoverProgress.Level):N0}";
                Check(tipText?.Text == expectedTip, "native hover shows the current selected growth run's experience and requirement");
                await Move(Control<TextBlock>(window, "PetName"));

                var dex = MenuTab(window, "dex");
                var box = MenuTab(window, "box");
                var preferences = MenuTab(window, "settings");
                await Click(preferences);
                await Wait(() => Control<StackPanel>(window, "SettingsPanel").IsVisible && Control<Border>(window, "Drawer").Height > 100, "settings tab");
                await Task.Delay(400);
                var before = settings.For(4).Exp;
                var beforeLevel = settings.For(4).Level;
                window.Activate();
                await NativeKey(repeat: true);
                await Wait(() => settings.For(4).Exp != before || settings.For(4).Level != beforeLevel, "native key input");
                Check(settings.For(4).Level == beforeLevel && settings.For(4).Exp == before + 1, "native keyboard autorepeat counts once");
                await Click(Control<CheckBox>(window, "FlipCheckBox"));
                Check(settings.FlipHorizontal, "native checkbox flips the pet");
                Check(((ScaleTransform)Control<Border>(window, "PetFacing").RenderTransform!).ScaleX == -1, "pet facing is flipped independently of text");
                await Click(Control<Button>(window, "ScaleUpButton"));
                Check(settings.UiScale == 4, "native scale arrow changes to 4x");
                await Click(Control<Button>(window, "ScaleDownButton"));
                Check(settings.UiScale == 2, "native scale arrow returns to 2x");
                await Click(Control<CheckBox>(window, "StartupCheckBox"));
                Check(startup.Writes == 1 && startup.Enabled, "startup registration changes only on checkbox click (isolated fake)");
                await Click(Control<CheckBox>(window, "StartupCheckBox"));
                Check(startup.Writes == 2 && !startup.Enabled, "startup can be disabled (isolated fake)");
                Capture(window, "02-settings");

                await Click(dex);
                await Wait(() => Control<StackPanel>(window, "DexPanel").IsVisible, "dex tab");
                await SelectGeneration(window, 0);
                await Wait(() => Control<WrapPanel>(window, "IconGrid").Children.Count == 1037, "all 1036 species and forms plus owned shiny");
                await Wait(() => !Field<bool>(window, "_dexLoading"), "all generation assets", 60000);
                Check(Control<WrapPanel>(window, "IconGrid").Children.Count == 1037, "all generations include every dex number and owned shiny forms");
                Capture(window, "03-all-generations");
                await Click(box);
                await Wait(() => box.IsChecked == true && Control<WrapPanel>(window, "IconGrid").Children.Count == 2, "owned box");
                Check(Control<CheckBox>(window, "OwnedOnly").IsChecked == true && !Control<CheckBox>(window, "OwnedOnly").IsEnabled,
                    "box keeps the owned filter enabled and locked");
                Check(Control<WrapPanel>(window, "IconGrid").Children.Count == 2, "box shows normal and shiny possessions together");
                await Click(Control<CheckBox>(window, "ShinyDex"));
                await Wait(() => !Field<bool>(window, "_dexLoading"), "shiny dex", 60000);
                await Click((RadioButton)Control<WrapPanel>(window, "IconGrid").Children[0]);
                await Wait(() => settings.SelectedShiny, "shiny selection", 60000);
                Check(Control<TextBlock>(window, "LevelText").Text!.StartsWith("★"), "selected shiny has separate level display");
                Check(!string.IsNullOrWhiteSpace(Control<TextBlock>(window, "DexDetailDescription").Text), "detail pane shows offline species information");
                Capture(window, "04-shiny-detail");
                await Click(Control<CheckBox>(window, "ShinyDex"));
                await Wait(() => !Field<bool>(window, "_dexLoading"), "normal dex");
                await Click((RadioButton)Control<WrapPanel>(window, "IconGrid").Children[0]);
                await Wait(() => !settings.SelectedShiny, "normal selection", 60000);

                await Click(box);
                await Task.Delay(400);
                settings.For(4).Level = 15;
                settings.For(4).Exp = 449;
                Invoke(window, "UpdateLevelUi");
                window.Activate();
                await NativeKey();
                await Wait(() => settings.SelectedDex == 5 && !Field<bool>(window, "_evolving"), "Charmander evolves", 60000);
                Check(settings.Owned.Contains(4) && settings.Owned.Contains(5), "evolution preserves the earlier form");
                Check(!settings.ShinyOwned.Contains(5), "normal evolution does not award the shiny form");
                Capture(window, "05-evolved");
                CheckPetSizeAndGrounding(window, "Charmeleon", 86);

                await Click(dex);
                await Task.Delay(400);
                settings.For(4).Level = 36;
                Invoke(window, "RefreshEvolutionUi");
                var grid = Control<WrapPanel>(window, "IconGrid");
                RadioButton Cell(int number) => grid.Children.OfType<RadioButton>().First(c => (int)c.Tag!.GetType().GetProperty("Dex")!.GetValue(c.Tag)! == number);
                await Click(Cell(4));
                await Wait(() => settings.SelectedDex == 4, "earlier form can be selected");
                Check(!settings.Owned.Contains(6), "earlier form does not skip the intermediate evolution");
                Check(ReferenceEquals(settings.For(4), settings.For(5)), "earlier and current appearances share the same growth run");
                Check(Cell(5).Classes.Contains("evolvable"), "current intermediate is highlighted when the shared run is ready");
                Check(Control<TextBlock>(window, "EvolutionNoticeText").Text!.Contains("리자드"), "earlier appearance names its actual evolution source");
                await Click(dex);
                await Task.Delay(400);
                await Click(Control<Button>(window, "EvolutionNotice"));
                await Wait(() => dex.IsChecked == true && !Field<bool>(window, "_dexLoading") &&
                    (int)Invoke(window, "CheckedGen")! == 1, "earlier appearance notice opens the source generation");
                await Task.Delay(400);
                Check(settings.SelectedDex == 4 && !settings.HasOwned(6), "notice opens the dex without skipping or changing the selected appearance");
                Check(Cell(5).Classes.Contains("evolvable"), "notice highlights the current appearance in the opened dex");
                Capture(window, "06-evolution-ready");
                await Click(Cell(5));
                await Wait(() => settings.SelectedDex == 6 && !Field<bool>(window, "_evolving"), "selecting intermediate form evolves to Charizard", 60000);
                await Task.Delay(300);
                CheckPetSizeAndGrounding(window, "Charizard", 94);
                Capture(window, "13-charizard-size");

                await Click(Control<CheckBox>(window, "ShinyDex"));
                await Wait(() => !Field<bool>(window, "_dexLoading"), "shiny evolution dex", 60000);
                await Click(Cell(4));
                await Wait(() => settings.SelectedDex == 4 && settings.SelectedShiny, "select shiny Charmander", 60000);
                var normalGrowth = settings.Progress.ToDictionary(pair => pair.Key, pair => (pair.Value.Level, pair.Value.Exp));
                var normalOwned = settings.Owned.ToHashSet();
                var normalLinks = settings.GrowthLinks.ToDictionary(pair => pair.Key, pair => pair.Value);
                settings.For(4, true).Level = 15;
                settings.For(4, true).Exp = 449;
                Invoke(window, "UpdateLevelUi");
                await NativeKey();
                var evolution = Control<EvolutionEffect>(window, "EvolutionVisual");
                foreach (var phase in new[] { (.2 / EvolutionEffect.DurationSeconds, "10-evolution-glow"), (.52, "11-evolution-silhouette"), (.90, "12-evolution-reveal") })
                {
                    await Wait(() => evolution.HasFrames && evolution.Progress >= phase.Item1, "evolution animation phase " + phase.Item2, 60000);
                    Check(Field<bool>(window, "_evolving"), "evolution remains active during " + phase.Item2);
                    Capture(window, phase.Item2);
                }
                await Wait(() => settings.SelectedDex == 5 && settings.SelectedShiny && !Field<bool>(window, "_evolving"), "shiny Charmander evolves to shiny Charmeleon", 60000);
                settings.For(5, true).Level = 35;
                settings.For(5, true).Exp = 1049;
                Invoke(window, "UpdateLevelUi");
                await NativeKey();
                await Wait(() => settings.SelectedDex == 6 && settings.SelectedShiny && !Field<bool>(window, "_evolving"), "shiny Charmeleon evolves to shiny Charizard", 60000);
                Check(new[] { 4, 5, 6 }.All(settings.ShinyOwned.Contains), "shiny evolution keeps all three shiny forms");
                Check(normalOwned.SetEquals(settings.Owned) && normalGrowth.Count == settings.Progress.Count &&
                    normalGrowth.All(pair => settings.Progress.TryGetValue(pair.Key, out var record) &&
                        pair.Value == (record.Level, record.Exp)), "shiny evolution leaves normal ownership, levels and experience unchanged");
                Check(normalLinks.Count == settings.GrowthLinks.Count && normalLinks.All(pair =>
                    settings.GrowthLinks.TryGetValue(pair.Key, out var key) && key == pair.Value),
                    "shiny evolution leaves all normal growth-run links unchanged");
                Check(Control<TextBlock>(window, "LevelText").Text!.StartsWith("★"), "fully evolved shiny retains its shiny indicator");
                await Task.Delay(400);
                Capture(window, "08-shiny-evolved");
                CheckPetSizeAndGrounding(window, "shiny Charizard", 94);
                await Click(dex);
                await Task.Delay(400);
                await Click(Control<Canvas>(window, "EggStage"));
                await Wait(() => settings.ShinyOwned.Contains(7), "hatch persisted reward", 60000);
                await Wait(() => Control<Canvas>(window, "ResultStage").IsVisible && Control<Image>(window, "ResultImage").Source != null, "hatch result animation", 60000);
                Check(settings.Eggs == 0 && settings.PendingEgg != null, "hatch consumes exactly one egg and prepares the next one");
                Capture(window, "07-hatched");
                await Wait(() => !Control<Canvas>(window, "ResultStage").IsVisible, "result returns to waiting", 60000);
                await Task.Delay(300);
                await CheckBranchAndRegionalPlay(window, settings);
                window.Position = new PixelPoint(700, 500);
                await Task.Delay(100);
                var origin = window.Position;
                var height = window.Bounds.Height;
                await Click(dex);
                await Task.Delay(400);
                var rise = (int)Math.Round((window.Bounds.Height - height) * window.DesktopScaling);
                Check(window.Position.X == origin.X, "opening dex preserves horizontal placement");
                await Click(dex);
                await Task.Delay(400);
                Check(window.Position == origin, "closing dex restores exact position after evolution and hatch");
                Check(SaveHash() == originalSave, "real player save stayed unchanged during native play");
                Console.WriteLine("NATIVE_PLAY_PASS");
            }
            catch (Exception ex)
            {
                try { Capture(window, "failure"); } catch { }
                Console.Error.WriteLine(ex);
                Environment.ExitCode = 1;
            }
            finally
            {
                // Check this even when an earlier assertion fails, rather than only on the success path.
                try { Check(SaveHash() == originalSave, "real player save remained unchanged at native play shutdown"); }
                catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
                desktop.Shutdown(Environment.ExitCode);
            }
        };
    }

    private static async Task CaptureReferenceViews(MainWindow window, Settings settings, FakeStartup startup)
    {
        Check(settings.Owned.SetEquals([4]) && settings.ShinyOwned.Count == 0,
            "reference starts with only normal Charmander");
        Check(settings.PendingEgg is { Kind: EggKind.Common, IsShiny: false } && settings.Eggs == 0,
            "reference starts with a waiting common egg");
        CheckPetSizeAndGrounding(window, "reference Charmander", 76);

        var dex = MenuTab(window, "dex");
        await Click(dex);
        await Wait(() => dex.IsChecked == true && Control<StackPanel>(window, "DexPanel").IsVisible,
            "reference dex tab");
        await SettleDrawer();
        await SelectGeneration(window, 1);
        await SettleDrawer();
        Check(Control<WrapPanel>(window, "IconGrid").Children.Count == 151, "reference dex shows all first-generation slots");
        await Task.Delay(100); // Let the native frame render; keep the real sprite/timer animations running.
        Capture(window, "reference-ui");

        var box = MenuTab(window, "box");
        await Click(box);
        await Wait(() => box.IsChecked == true && Control<WrapPanel>(window, "IconGrid").Children.Count == 1,
            "reference box tab");
        await SettleDrawer();
        Check(Control<CheckBox>(window, "OwnedOnly").IsChecked == true && !Control<CheckBox>(window, "OwnedOnly").IsEnabled,
            "reference box locks the owned filter");
        Capture(window, "reference-box");

        var preferences = MenuTab(window, "settings");
        await Click(preferences);
        await Wait(() => preferences.IsChecked == true && Control<StackPanel>(window, "SettingsPanel").IsVisible,
            "reference settings tab");
        await SettleDrawer();
        Check(startup.Writes == 0, "reference settings opens without changing startup registration");
        Capture(window, "reference-settings");

        async Task SettleDrawer()
        {
            (Size Size, PixelPoint Position)? previous = null;
            var stable = 0;
            await Wait(() =>
            {
                window.UpdateLayout();
                var target = Field<double>(window, "_drawerTargetHeight");
                if (target <= 0 || Field<bool>(window, "_drawerRemeasurePending") ||
                    Math.Abs(Control<Border>(window, "Drawer").Height - target) > .01)
                {
                    previous = null;
                    stable = 0;
                    return false;
                }
                var current = (window.Bounds.Size, window.Position);
                stable = previous == current ? stable + 1 : 1;
                previous = current;
                return stable >= 3;
            }, "reference drawer layout settles");
        }
    }

    private static async Task CheckBranchAndRegionalPlay(MainWindow window, Settings settings)
    {
        var dexTab = MenuTab(window, "dex");
        async Task OpenDex()
        {
            if (dexTab.IsChecked != true) await Click(dexTab);
            await Wait(() => dexTab.IsChecked == true && Control<Border>(window, "Drawer").Height > 100, "open branch test dex");
            await Task.Delay(400);
        }
        async Task CloseDex()
        {
            if (dexTab.IsChecked == true) await Click(dexTab);
            await Task.Delay(400);
        }
        Task Generation(int generation) => SelectGeneration(window, generation);

        // This mutates only NewPreview state; real clicks perform selection, level-up and evolution.
        settings.AddOwned(133);
        settings.For(133).Level = 24;
        settings.For(133).Exp = Settings.ExpToNext(24) - 1;
        await OpenDex();
        if (Control<CheckBox>(window, "ShinyDex").IsChecked == true)
            await Click(Control<CheckBox>(window, "ShinyDex"));
        await Generation(1);
        Invoke(window, "RefreshIconCell", 133, false);
        await Click(FindCell(window, 133, false));
        await Wait(() => settings.SelectedDex == 133 && !settings.SelectedShiny &&
            Field<int>(window, "_pendingSelections") == 0, "select Eevee", 60000);
        var shinyBefore = JsonSerializer.Serialize(new { settings.ShinyOwned, settings.ShinyProgress, settings.ShinyGrowthLinks });
        await NativeKey();
        await Wait(() => settings.For(133).PendingEvolution.HasValue && Field<bool>(window, "_evolving"), "Eevee randomly prepares one branch", 60000);
        var firstTarget = settings.For(133).PendingEvolution!.Value;
        Check(EvolutionData.From(133).Any(rule => rule.ToId == firstTarget), "first Eevee target belongs to its eight level-25 branches");
        Check(!Control<WrapPanel>(window, "EvolutionChoices").IsVisible, "random branch evolution requires no choice dialog");
        Check(!settings.HasOwned(firstTarget), "random target is prepared before evolution awards ownership");
        Capture(window, "14-eevee-prepared");
        await Wait(() => settings.SelectedDex == firstTarget && !Field<bool>(window, "_evolving"), "first random Eevee evolution", 60000);
        Check(ReferenceEquals(settings.For(133), settings.For(firstTarget)), "Eevee and its result share one growth run");
        Capture(window, "15-eevee-first-branch");

        var firstRun = settings.For(firstTarget);
        settings.PendingEgg = new(EggKind.Common, 133, false);
        settings.Eggs = 1;
        var eggState = typeof(MainWindow).GetNestedType("EggState", BindingFlags.NonPublic)!;
        Invoke(window, "SetEggState", Enum.Parse(eggState, "Ready"));
        await CloseDex();
        await Click(Control<Canvas>(window, "EggStage"));
        await Wait(() => !ReferenceEquals(settings.For(133), firstRun) &&
            Control<Canvas>(window, "ResultStage").IsVisible, "duplicate Eevee egg starts a new run", 60000);
        Check(settings.For(133).Level == 1 && settings.For(133).Exp == 0, "Eevee rearing restarts at level one and zero experience");
        Check(ReferenceEquals(settings.For(firstTarget), firstRun), "rehatching keeps the previous Eevee evolution's run");
        Check(Control<TextBlock>(window, "NewText").Text == "새 육성 · Lv.1", "hatch result identifies the new growth run");
        Capture(window, "16-eevee-rearing");
        await Wait(() => !Control<Canvas>(window, "ResultStage").IsVisible, "Eevee result returns to waiting", 60000);

        await OpenDex();
        await Generation(1);
        await Click(FindCell(window, 133, false));
        await Wait(() => settings.SelectedDex == 133 && !settings.SelectedShiny &&
            Field<int>(window, "_pendingSelections") == 0, "select newly reared Eevee", 60000);
        var previousGrowth = (firstRun.Level, firstRun.Exp);
        settings.For(133).Level = 24;
        settings.For(133).Exp = Settings.ExpToNext(24) - 1;
        Invoke(window, "UpdateLevelUi");
        await NativeKey();
        await Wait(() => settings.For(133).PendingEvolution.HasValue && Field<bool>(window, "_evolving"), "reared Eevee prepares a remaining branch", 60000);
        var secondTarget = settings.For(133).PendingEvolution!.Value;
        Check(secondTarget != firstTarget && settings.EvolutionOptions(133).Length == 7,
            "second Eevee target is randomly selected only from seven uncollected branches");
        await Wait(() => settings.SelectedDex == secondTarget && !Field<bool>(window, "_evolving"), "second random Eevee evolution", 60000);
        Check(previousGrowth == (firstRun.Level, firstRun.Exp), "new Eevee run leaves the earlier branch's growth unchanged");
        Check(!ReferenceEquals(firstRun, settings.For(secondTarget)), "two Eevee branches retain independent growth runs");
        Check(shinyBefore == JsonSerializer.Serialize(new { settings.ShinyOwned, settings.ShinyProgress, settings.ShinyGrowthLinks }),
            "normal Eevee evolution and rearing preserve all shiny ownership and growth");
        Capture(window, "17-eevee-second-branch");

        settings.AddOwned(8194); // Paldean Wooper, distinct from ordinary #194.
        await Generation(9);
        await Click(FindCell(window, 8194, false));
        await Wait(() => settings.SelectedDex == 8194 && !settings.SelectedShiny &&
            Control<Image>(window, "Sprite").Source != null && Field<int>(window, "_pendingSelections") == 0,
            "select Paldean Wooper from generation nine", 60000);
        Check(!settings.HasOwned(194), "regional selection does not grant the ordinary species");
        Check(Control<TextBlock>(window, "DexDetailName").Text == "팔데아 우파", "regional detail uses its own appearance name");
        Check(Control<TextBlock>(window, "DexDetailTypes").Text == "독 · 땅", "regional detail shows Paldean rather than ordinary types");
        Check(Control<TextBlock>(window, "DexDetailNumber").Text!.Contains("0194"), "regional detail displays its national dex number");
        CheckPetSizeAndGrounding(window, "Paldean Wooper", 72);
        Capture(window, "18-regional-selection");
        await CloseDex();
    }

    private static RadioButton FindCell(MainWindow window, int dex, bool shiny) =>
        Control<WrapPanel>(window, "IconGrid").Children.OfType<RadioButton>().Single(cell =>
            (int)cell.Tag!.GetType().GetProperty("Dex")!.GetValue(cell.Tag)! == dex &&
            (bool)cell.Tag.GetType().GetProperty("IsShiny")!.GetValue(cell.Tag)! == shiny);

    private static void CheckPetSizeAndGrounding(MainWindow window, string species, double expectedHeight)
    {
        window.UpdateLayout();
        var atlas = Field<SpriteAtlas>(window, "_atlas");
        var scale = ((ScaleTransform)Control<LayoutTransformControl>(window, "StageZoom").LayoutTransform!).ScaleX;
        Check(Math.Abs(atlas.Body.Height * scale - expectedHeight) < .001, species + $" uses its {expectedHeight} DIP display height");
        Check(atlas.FootAlignedWidth * scale <= 212.001, species + " fits within the pet column");
        var index = Field<int>(window, "_frame");
        var anchor = atlas.FootAnchorFor(index);
        var frame = atlas.Frames[index];
        var foot = Control<Image>(window, "Sprite").TranslatePoint(new Point(anchor.X - frame.OffsetX, anchor.Y - frame.OffsetY), window)!.Value;
        var shadow = Control<Image>(window, "PetShadow").TranslatePoint(new Point(49, 8.5), window)!.Value;
        Check(new Vector(foot.X - shadow.X, foot.Y - shadow.Y).Length < 1, species + " feet meet the painted shadow center");
        Console.WriteLine($"PET {species}: visibleHeight={atlas.Body.Height * scale:F3} width={atlas.FootAlignedWidth * scale:F3} foot={foot} shadow={shadow}");
    }

    private static async Task Move(Control control)
    {
        var point = control.PointToScreen(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2));
        Mouse(5, point);
        await Task.Delay(450);
    }
    private static async Task SelectGeneration(MainWindow window, int generation)
    {
        var picker = Control<Button>(window, "GenerationPickerButton");
        await Click(picker);
        await Wait(() => picker.Flyout?.IsOpen == true, "generation picker opens");
        var target = Control<StackPanel>(window, "GenTabs").Children.OfType<RadioButton>()
            .Single(tab => Equals(tab.Tag, generation));
        await Wait(() => target.IsEffectivelyVisible && target.Bounds.Width > 0 && target.Bounds.Height > 0,
            "generation choice is visible");
        await Click(target);
        await Wait(() => picker.Flyout?.IsOpen == false && !Field<bool>(window, "_dexLoading") &&
            (int)Invoke(window, "CheckedGen")! == generation, $"generation {generation} artwork", 60000);
    }
    private static async Task Click(Control control)
    {
        var point = await ClickPointAfterScrolling(control);
        Console.WriteLine($"CLICK {control.Name ?? control.GetType().Name} at {point}");
        Mouse(5, point);
        Mouse(1, point);
        await Task.Delay(60);
        Mouse(2, point);
        await Task.Delay(180);
    }
    private static async Task<PixelPoint> ClickPointAfterScrolling(Control control)
    {
        // RefreshIconCell replaces every radio button. A newly inserted cell has no
        // arranged bounds yet, so bringing it into view before layout targets (0,0)
        // instead of its eventual row. Arrange first and wait for the nested viewports
        // and the native window position to settle before sending a screen click.
        var end = DateTime.UtcNow.AddSeconds(5);
        PixelPoint? previous = null;
        var stable = 0;
        while (DateTime.UtcNow < end)
        {
            var topLevel = TopLevel.GetTopLevel(control)
                ?? throw new InvalidOperationException("Cannot click a control detached from its native window.");
            topLevel.UpdateLayout();
            if (control.Bounds.Width > 0 && control.Bounds.Height > 0)
            {
                control.BringIntoView();
                topLevel.UpdateLayout();
            }
            await Task.Delay(40);
            topLevel.UpdateLayout();
            var center = new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
            var visible = control.IsEffectivelyVisible && control.IsEffectivelyEnabled &&
                control.Bounds.Width > 0 && control.Bounds.Height > 0;
            foreach (var clip in control.GetVisualAncestors().Where(ancestor => ancestor.ClipToBounds))
            {
                var inClip = control.TranslatePoint(center, clip);
                visible &= inClip.HasValue && new Rect(clip.Bounds.Size).Contains(inClip.Value);
            }
            var inWindow = control.TranslatePoint(center, topLevel);
            visible &= inWindow.HasValue && new Rect(topLevel.Bounds.Size).Contains(inWindow.Value);
            var point = control.PointToScreen(center);
            var screens = topLevel.Screens
                ?? throw new InvalidOperationException("Cannot verify a native click without monitor information.");
            visible &= screens.All.Any(screen => screen.Bounds.Contains(point));
            stable = visible && previous == point ? stable + 1 : visible ? 1 : 0;
            previous = visible ? point : null;
            if (stable >= 3) return point;
        }
        var scrolling = string.Join("; ", control.GetVisualAncestors().OfType<ScrollViewer>().Select(scroll =>
            $"{scroll.Name}: offset={scroll.Offset}, viewport={scroll.Viewport}, extent={scroll.Extent}"));
        throw new InvalidOperationException($"Cannot scroll {control.Name ?? control.GetType().Name} to a visible, stable click point. " +
            $"Bounds={control.Bounds}; {scrolling}");
    }
    private static async Task NativeKey(bool repeat = false)
    {
        var down = CGEventCreateKeyboardEvent(IntPtr.Zero, 0, true);
        CGEventPost(0, down); CFRelease(down);
        await Task.Delay(60);
        if (repeat)
            for (var i = 0; i < 3; i++)
            {
                var again = CGEventCreateKeyboardEvent(IntPtr.Zero, 0, true);
                CGEventSetIntegerValueField(again, 8, 1);
                CGEventPost(0, again); CFRelease(again);
                await Task.Delay(30);
            }
        var up = CGEventCreateKeyboardEvent(IntPtr.Zero, 0, false);
        CGEventPost(0, up); CFRelease(up);
        await Task.Delay(150);
    }
    private static void Mouse(int type, PixelPoint point)
    {
        var e = CGEventCreateMouseEvent(IntPtr.Zero, type, new NativePoint(point.X, point.Y), 0);
        CGEventPost(0, e); CFRelease(e);
    }
    private static void Capture(Window window, string name)
    {
        Directory.CreateDirectory(Output);
        window.UpdateLayout();
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.Bounds.Width * 2), (int)Math.Ceiling(window.Bounds.Height * 2)), new Vector(192, 192));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(Output, name + ".png"));
    }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativePoint(double X, double Y);
    private const string Graphics = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    [DllImport(Graphics)] private static extern IntPtr CGEventCreateMouseEvent(IntPtr source, int type, NativePoint point, int button);
    [DllImport(Graphics)] private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort key, [MarshalAs(UnmanagedType.I1)] bool down);
    [DllImport(Graphics)] private static extern void CGEventSetIntegerValueField(IntPtr ev, int field, long value);
    [DllImport(Graphics)] private static extern void CGEventPost(int tap, IntPtr ev);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(IntPtr value);
}
