using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
        var originalSave = SaveHash();
        var settings = (Settings)typeof(Settings).GetMethod("NewPreview", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [4])!;
        settings.For(4).Exp = 29;
        settings.AddOwned(4, true);
        settings.PendingEgg = new(EggKind.Rare, 7, true);
        settings.Eggs = 1;
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
                Capture(window, "01-collapsed");
                CheckPetSizeAndGrounding(window, "Charmander");
                var track = Control<Border>(window, "ExpTrack");
                for (var attempt = 0; attempt < 5 && !ToolTip.GetIsOpen(track); attempt++)
                {
                    await Move(Control<TextBlock>(window, "PetName"));
                    await Move(track);
                }
                await Wait(() => ToolTip.GetIsOpen(track), "experience hover");
                var tip = (ToolTip)ToolTip.GetTip(track)!;
                var tipText = ((Decorator)tip.Content!).Child as TextBlock;
                Check(tipText!.Text!.Contains("필요 경험치: 30"), "native hover shows current/required experience");
                await Move(Control<TextBlock>(window, "PetName"));

                var menu = Control<StackPanel>(window, "MenuTabs");
                var dex = (ToggleButton)menu.Children[0];
                var preferences = (ToggleButton)menu.Children[1];
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
                var tabs = Control<StackPanel>(window, "GenTabs");
                await Click((RadioButton)tabs.Children[0]);
                await Wait(() => Control<WrapPanel>(window, "IconGrid").Children.Count == 1026, "all 1025 species plus owned shiny");
                await Wait(() => !Field<bool>(window, "_dexLoading"), "all generation assets", 60000);
                Check(Control<WrapPanel>(window, "IconGrid").Children.Count == 1026, "all generations include every dex number and owned shiny forms");
                Capture(window, "03-all-generations");
                await Click(Control<CheckBox>(window, "OwnedOnly"));
                Check(Control<WrapPanel>(window, "IconGrid").Children.Count == 2, "owned filter shows normal and shiny forms together");
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

                await Click(dex);
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
                CheckPetSizeAndGrounding(window, "Charmeleon");

                await Click(dex);
                await Task.Delay(400);
                settings.For(4).Level = 36;
                Invoke(window, "RefreshEvolutionUi");
                var grid = Control<WrapPanel>(window, "IconGrid");
                RadioButton Cell(int number) => grid.Children.OfType<RadioButton>().First(c => (int)c.Tag!.GetType().GetProperty("Dex")!.GetValue(c.Tag)! == number);
                await Click(Cell(4));
                await Wait(() => settings.SelectedDex == 4, "earlier form can be selected");
                Check(!settings.Owned.Contains(6), "earlier form does not skip the intermediate evolution");
                Check(Cell(5).Classes.Contains("evolvable"), "intermediate form is highlighted when ancestors have enough levels");
                Capture(window, "06-evolution-ready");
                await Click(Cell(5));
                await Wait(() => settings.SelectedDex == 6 && !Field<bool>(window, "_evolving"), "selecting intermediate form evolves to Charizard", 60000);
                await Task.Delay(300);
                CheckPetSizeAndGrounding(window, "Charizard");
                Capture(window, "13-charizard-size");

                await Click(Control<CheckBox>(window, "ShinyDex"));
                await Wait(() => !Field<bool>(window, "_dexLoading"), "shiny evolution dex", 60000);
                await Click(Cell(4));
                await Wait(() => settings.SelectedDex == 4 && settings.SelectedShiny, "select shiny Charmander", 60000);
                var normalGrowth = settings.Progress.ToDictionary(pair => pair.Key, pair => (pair.Value.Level, pair.Value.Exp));
                var normalOwned = settings.Owned.ToHashSet();
                settings.For(4, true).Level = 15;
                settings.For(4, true).Exp = 449;
                Invoke(window, "UpdateLevelUi");
                await NativeKey();
                var evolution = Control<EvolutionEffect>(window, "EvolutionVisual");
                foreach (var phase in new[] { (.14, "10-evolution-glow"), (.52, "11-evolution-silhouette"), (.90, "12-evolution-reveal") })
                {
                    await Wait(() => evolution.HasFrames && evolution.Progress >= phase.Item1, "evolution animation phase " + phase.Item2, 60000);
                    Check(Field<bool>(window, "_evolving"), "evolution keeps interaction locked during " + phase.Item2);
                    Capture(window, phase.Item2);
                }
                await Wait(() => settings.SelectedDex == 5 && settings.SelectedShiny && !Field<bool>(window, "_evolving"), "shiny Charmander evolves to shiny Charmeleon", 60000);
                settings.For(5, true).Level = 35;
                settings.For(5, true).Exp = 1049;
                Invoke(window, "UpdateLevelUi");
                await NativeKey();
                await Wait(() => settings.SelectedDex == 6 && settings.SelectedShiny && !Field<bool>(window, "_evolving"), "shiny Charmeleon evolves to shiny Charizard", 60000);
                Check(new[] { 4, 5, 6 }.All(settings.ShinyOwned.Contains), "shiny evolution keeps all three shiny forms");
                Check(normalOwned.SetEquals(settings.Owned) && normalGrowth.All(pair =>
                    pair.Value == (settings.For(pair.Key).Level, settings.For(pair.Key).Exp)), "shiny evolution leaves normal ownership, levels and experience unchanged");
                Check(Control<TextBlock>(window, "LevelText").Text!.StartsWith("★"), "fully evolved shiny retains its shiny indicator");
                await Task.Delay(400);
                Capture(window, "08-shiny-evolved");
                CheckPetSizeAndGrounding(window, "shiny Charizard");
                await Click(dex);
                await Task.Delay(400);
                await Click(Control<Canvas>(window, "EggStage"));
                await Wait(() => settings.ShinyOwned.Contains(7), "hatch persisted reward", 60000);
                await Wait(() => Control<Canvas>(window, "ResultStage").IsVisible && Control<Image>(window, "ResultImage").Source != null, "hatch result animation", 60000);
                Check(settings.Eggs == 0 && settings.PendingEgg != null, "hatch consumes exactly one egg and prepares the next one");
                Capture(window, "07-hatched");
                await Wait(() => !Control<Canvas>(window, "ResultStage").IsVisible, "result returns to waiting", 60000);
                await Task.Delay(300);
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
            finally { desktop.Shutdown(Environment.ExitCode); }
        };
    }

    private static void CheckPetSizeAndGrounding(MainWindow window, string species)
    {
        window.UpdateLayout();
        var atlas = Field<SpriteAtlas>(window, "_atlas");
        var scale = ((ScaleTransform)Control<LayoutTransformControl>(window, "StageZoom").LayoutTransform!).ScaleX;
        Check(Math.Abs(atlas.Body.Height * scale - 110) < .001, species + " uses the shared 110 DIP visible height");
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
    private static async Task Click(Control control)
    {
        control.BringIntoView();
        await Task.Delay(120);
        var point = control.PointToScreen(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2));
        Console.WriteLine($"CLICK {control.Name ?? control.GetType().Name} at {point}");
        Mouse(5, point);
        Mouse(1, point);
        await Task.Delay(60);
        Mouse(2, point);
        await Task.Delay(180);
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
