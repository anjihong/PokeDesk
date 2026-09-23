using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskPokemon;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;
    private static readonly string Temporary = Path.Combine(Path.GetTempPath(), "PokeDesk-ui-" + Guid.NewGuid().ToString("N"));
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        Directory.CreateDirectory(Temporary);
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var transport = new Images();
            typeof(SpriteAtlas).GetProperty("CacheDirectory", BindingFlags.Static | BindingFlags.NonPublic)!
                .SetValue(null, Path.Combine(Temporary, "cache"));
            typeof(SpriteAtlas).GetProperty("Http", BindingFlags.Static | BindingFlags.NonPublic)!
                .SetValue(null, new HttpClient(transport));

            var normal = Await(SpriteAtlas.LoadAsync(4));
            var shiny = Await(SpriteAtlas.LoadAsync(4, true));
            Check(normal.Frames.Length == 2 && shiny.Frames.Length == 2, "both colors animate");
            Check(Pixel(normal.Frames[0].Bitmap) != Pixel(shiny.Frames[0].Bitmap), "colors differ");
            var firstRequests = transport.Count("pokemon/shiny/4.json") + transport.Count("pokemon/shiny/4.png");
            Await(SpriteAtlas.LoadAsync(4, true));
            Check(transport.Count("pokemon/shiny/4.json") + transport.Count("pokemon/shiny/4.png") == firstRequests,
                "successful atlas files reuse disk cache while unavailable expanded path can retry");
            transport.DisableExpandedShiny = true;
            Check(Await(SpriteAtlas.LoadAsync(991, true)).Frames.Length == 1, "Iron Bundle fixture reproduces cached static shiny");
            transport.DisableExpandedShiny = false;
            Check(Await(SpriteAtlas.LoadAsync(991, true)).Frames.Length == 2, "expanded shiny animation overrides cached static atlas");
            Check(transport.Count("pokemon/exp/shiny/991.png") == 1, "expanded shiny uses its own cache path");
            Check(Await(SpriteAtlas.LoadAsync(133, true)).Frames.Length == 2, "expanded shiny timeout falls back to basic shiny atlas");
            var concurrent = Enumerable.Range(0, 4).Select(_ => SpriteAtlas.LoadAsync(7, true)).ToArray();
            Await(Task.WhenAll(concurrent));
            Check(transport.Count("pokemon/exp/shiny/7.png") == 1 && transport.Count("pokemon/exp/shiny/7.json") == 1,
                "concurrent downloads deduplicated");
            Check(Await(SpriteAtlas.LoadAsync(25, true)).Frames.Length == 1, "404 falls back to static shiny");
            Check(Await(SpriteAtlas.LoadAsync(26, true)).Frames.Length == 1, "timeout falls back to static shiny");
            var failed = false;
            try { Await(SpriteAtlas.LoadAsync(27, true)); } catch (HttpRequestException) { failed = true; }
            Check(failed, "missing shiny is not replaced by normal");

            var icons = Await(PokemonIcons.LoadGenAsync(1));
            var shinyIcons = Await(PokemonIcons.LoadGenAsync(1, true));
            Check(icons.Count == 151 && shinyIcons.Count == 151, "both icon collections");
            Check(Pixel(icons[4]) != Pixel(shinyIcons[4]), "shiny icon matches color");
            var artType = typeof(MainWindow).Assembly.GetType("DeskPokemon.EggArtwork")!;
            foreach (var kind in Enum.GetValues<EggKind>())
            {
                var f = Await((Task<SpriteFrame>)artType.GetMethod("LoadAsync")!.Invoke(null, [kind])!);
                Check(f.Width == 28 && f.Height == 30, "egg art normalized");
            }

            var s = new Settings
            {
                SchemaVersion = 2, StarterDex = 4, SelectedDex = 4,
                Owned = [4, 7], ShinyOwned = [4], PendingEgg = new(EggKind.Rare, 4, true)
            };
            typeof(Settings).GetField("savePath", Private)!.SetValue(s, Path.Combine(Temporary, "settings.json"));
            s.Save();
            var window = new MainWindow(s);
            // Exercise real controls without showing a desktop window or starting global input hooks.
            Call(window, "BuildGenTabs");
            Call(window, "SelectGenTab", 1);
            Until(() => Element<Panel>(window, "IconGrid").Children.Count == 151);
            var ownedOnly = Element<CheckBox>(window, "OwnedOnly");
            var shinyFilter = Element<CheckBox>(window, "ShinyDex");
            ownedOnly.IsChecked = true;
            Check(Element<Panel>(window, "IconGrid").Children.Count == 2, "owned normal filter");
            shinyFilter.IsChecked = true;
            Await((Task)Call(window, "RefreshDexAsync")!);
            Call(window, "UpdateOwnedCount");
            Check(Element<Panel>(window, "IconGrid").Children.Count == 1, "owned shiny filter");
            Check(s.SelectedDex == 4 && !s.SelectedShiny, "filter does not change current pet");
            var cell = (RadioButton)Element<Panel>(window, "IconGrid").Children[0];
            cell.IsChecked = true;
            Until(() => s.SelectedShiny);
            var oldExp = s.For(4).Exp;
            Call(window, "AddExp");
            Check(s.For(4).Exp == oldExp && s.For(4, true).Exp == 1, "selected color receives experience");
            ownedOnly.IsChecked = false;
            Check(Element<Panel>(window, "IconGrid").Children.Count == 151, "all shiny filter");

            // Both controls must fit on the same row without overlapping the count.
            var root = Element<FrameworkElement>(window, "Root");
            Element<FrameworkElement>(window, "Drawer").Height = 215;
            Layout(root);
            var left = ownedOnly.TransformToAncestor(root).TransformBounds(new Rect(ownedOnly.RenderSize));
            var right = shinyFilter.TransformToAncestor(root).TransformBounds(new Rect(shinyFilter.RenderSize));
            var count = Element<TextBlock>(window, "OwnedCount");
            var countBounds = count.TransformToAncestor(root).TransformBounds(new Rect(count.RenderSize));
            Check(Math.Abs(left.Y - right.Y) < 1 && left.Right <= right.Left, "checkboxes same row");
            Check(right.Right <= countBounds.Left, "filters do not overlap count");

            var debugMenu = window.ContextMenu!.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString()!.StartsWith("테스트 알"));
#if DEBUG
            Check(debugMenu != null, "Debug has test menu");
            var force = (MenuItem)debugMenu!.Items[0];
            var grants = debugMenu.Items.OfType<MenuItem>().Skip(1).ToArray();
            foreach (var kind in Enum.GetValues<EggKind>())
            {
                force.IsChecked = true;
                grants[(int)kind].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(s.Eggs == 1 && s.PendingEgg!.Kind == kind && s.PendingEgg.IsShiny, "chosen test egg forced shiny");
                Check(!force.IsChecked, "force option resets after grant");
            }
#else
            Check(debugMenu == null && typeof(Settings).GetMethod("GrantTestEgg") == null, "Release has no test grant");
#endif
            s.Eggs = 1;
            s.PendingEgg = new(EggKind.Rare, 4, true);
            s.Save();
            Await((Task)Call(window, "HatchAsync")!);
            Until(() => typeof(MainWindow).GetField("_resultAtlas", Private)!.GetValue(window) != null);
            var zoom = (ScaleTransform)window.FindName("ResultZoom");
            Check(zoom.ScaleX > 0 && zoom.ScaleX < 1, "large hatch sprite has fractional positive scale");
            Check(s.Eggs == 0 && s.For(4, true).Level == 2, "UI hatch commits duplicate color only");
            Call(window, "ShowResultFrame", 1);
            Await(Task.Delay(600));
            Layout(root);
            var output = Path.GetFullPath(Path.Combine("bin", "ui-verification"));
            Directory.CreateDirectory(output);
            Render(root, Path.Combine(output, "shiny-hatch.png"));
            ((DispatcherTimer)typeof(MainWindow).GetField("_resultTimer", Private)!.GetValue(window)!).Stop();
            transport.SlowDex = 1;
            s.Eggs = 1;
            s.PendingEgg = new(EggKind.Common, 1, true);
            s.Save();
            Await((Task)Call(window, "HatchAsync")!);
            Await(Task.Delay(5200));
            Check(typeof(MainWindow).GetField("_eggState", Private)!.GetValue(window)!.ToString() == "Result",
                "slow image download does not dismiss hatch result");
            Until(() => typeof(MainWindow).GetField("_resultAtlas", Private)!.GetValue(window) != null);
            Check(Element<Image>(window, "ResultImage").Source != null, "slow hatch image eventually displayed");
            ((DispatcherTimer)typeof(MainWindow).GetField("_resultTimer", Private)!.GetValue(window)!).Stop();
            if (args.Contains("--real-assets"))
                VerifyRealAssets(window, s, root, output);
            app.Shutdown();
            Console.WriteLine($"PASS: {checks} WPF integration assertions; render: {output}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(Temporary, recursive: true); }
    }

    private static void VerifyRealAssets(MainWindow window, Settings settings, FrameworkElement root, string output)
    {
        var fixtures = Path.GetFullPath(Path.Combine("bin", "asset-verification"));
        var cache = Path.Combine(Temporary, "real-assets");
        Directory.CreateDirectory(Path.Combine(cache, "pokemon", "shiny"));
        File.Copy(Path.Combine(fixtures, "shiny-4.json"), Path.Combine(cache, "pokemon", "shiny", "4.json"));
        File.Copy(Path.Combine(fixtures, "shiny-4.png"), Path.Combine(cache, "pokemon", "shiny", "4.png"));
        Directory.CreateDirectory(Path.Combine(cache, "pokemon", "exp", "shiny"));
        foreach (var ext in new[] { ".json", ".png" })
            File.Copy(Path.Combine(fixtures, "exp-shiny-991" + ext), Path.Combine(cache, "pokemon", "exp", "shiny", "991" + ext));
        var installedCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskPokemon", "sprites");
        for (var gen = 1; gen <= 9; gen++)
            foreach (var ext in new[] { ".json", ".png" })
                File.Copy(Path.Combine(installedCache, $"pokemon_icons_{gen}{ext}"), Path.Combine(cache, $"pokemon_icons_{gen}{ext}"));
        Directory.CreateDirectory(Path.Combine(cache, "egg"));
        foreach (var ext in new[] { ".json", ".png" })
            File.Copy(Path.Combine(installedCache, "egg", "egg" + ext), Path.Combine(cache, "egg", "egg" + ext));
        typeof(SpriteAtlas).GetProperty("CacheDirectory", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, cache);
        ((System.Collections.IDictionary)typeof(PokemonIcons).GetField("Cache", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Clear();
        var total = 0;
        for (var gen = 1; gen <= 9; gen++)
        {
            var normal = Await(PokemonIcons.LoadGenAsync(gen));
            var shiny = Await(PokemonIcons.LoadGenAsync(gen, true));
            Check(normal.Keys.Order().SequenceEqual(shiny.Keys.Order()), "real icon colors cover same species");
            total += shiny.Count;
        }
        Check(total == 1025, "real icons cover all 1025 species");
        var real = Await(SpriteAtlas.LoadAsync(4, true));
        Check(real.Frames.Length == 108, "real shiny Charmander atlas frames");
        var ironBundle = Await(SpriteAtlas.LoadAsync(991, true));
        Check(ironBundle.Frames.Length == 21, "real expanded shiny Iron Bundle has 21 frames");
        Check(ironBundle.Frames.Select(f => f.Bitmap.SourceRect).Distinct().Count() > 1,
            "real Iron Bundle animation uses different sheet frames");
        var gif = (SpriteAtlas)typeof(SpriteAtlas).GetMethod("ParseGif", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [Path.Combine(fixtures, "shiny-6.gif")])!;
        Check(gif.Frames.Length > 1, "real shiny Charizard GIF parsed");
        Check(Await((Task<bool>)Call(window, "LoadPokemonAsync", 4, true)!), "real shiny main sprite loaded");
        Call(window, "ShowFrame", 4);
        Element<CheckBox>(window, "OwnedOnly").IsChecked = false;
        Await((Task)Call(window, "RefreshDexAsync")!);
        settings.Eggs = 1;
        settings.PendingEgg = new(EggKind.Rare, 4, true);
        settings.Save();
        Await((Task)Call(window, "HatchAsync")!);
        Until(() => typeof(MainWindow).GetField("_resultAtlas", Private)!.GetValue(window) != null);
        Await(Task.Delay(600));
        ((DispatcherTimer)typeof(MainWindow).GetField("_resultTimer", Private)!.GetValue(window)!).Stop();
        Call(window, "ShowResultFrame", 12);
        Layout(root);
        Render(root, Path.Combine(output, "real-shiny-hatch.png"));
        var art = typeof(MainWindow).Assembly.GetType("DeskPokemon.EggArtwork")!;
        foreach (var name in new[] { "Cache", "Atlases" })
            ((System.Collections.IDictionary)art.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Clear();
        var state = typeof(MainWindow).GetNestedType("EggState", BindingFlags.NonPublic)!;
        foreach (var kind in Enum.GetValues<EggKind>())
        {
            settings.PendingEgg = new(kind, 4, true);
            settings.Eggs = 1;
            Call(window, "SetEggState", Enum.Parse(state, "Ready"));
            Until(() => Element<FrameworkElement>(window, "EggFallback").Visibility == Visibility.Collapsed);
            Layout(root);
            Render(root, Path.Combine(output, $"real-egg-{kind}.png"));
            Check(Element<Image>(window, "EggImage").Source != null, "real grade image rendered");
        }
    }

    private static object? Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, Private)!.Invoke(o, args);
    private static T Element<T>(MainWindow w, string name) where T : FrameworkElement => (T)w.FindName(name);
    private static void Layout(FrameworkElement root) { root.Measure(new Size(300, double.PositiveInfinity)); root.Arrange(new Rect(root.DesiredSize)); root.UpdateLayout(); }
    private static void Render(FrameworkElement root, string path)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(55, 60, 70)), null, new Rect(root.RenderSize));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(root.RenderSize));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); png.Save(file);
    }
    private static uint Pixel(BitmapSource b) { var bytes = new byte[4]; new FormatConvertedBitmap(b, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(0, 0, 1, 1), bytes, 4, 0); return BitConverter.ToUInt32(bytes); }
    private static T Await<T>(Task<T> t) { Until(() => t.IsCompleted); return t.GetAwaiter().GetResult(); }
    private static void Await(Task t) { Until(() => t.IsCompleted); t.GetAwaiter().GetResult(); }
    private static void Until(Func<bool> predicate)
    {
        if (predicate()) return;
        var frame = new DispatcherFrame();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (predicate() || watch.Elapsed.TotalSeconds > 20) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        if (!predicate()) throw new TimeoutException("WPF test timed out");
    }

    private sealed class Images : HttpMessageHandler
    {
        private readonly Dictionary<string, int> counts = new();
        public int SlowDex { get; set; }
        public bool DisableExpandedShiny { get; set; }
        public int Requests => counts.Values.Sum();
        public int Count(string suffix) => counts.Where(p => p.Key.EndsWith(suffix)).Sum(p => p.Value);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            counts[path] = counts.GetValueOrDefault(path) + 1;
            await Task.Delay(5, cancellationToken);
            if (path.Contains("/pokemon/exp/shiny/"))
            {
                if (path.Contains("/133.")) throw new TaskCanceledException("expanded fixture timeout");
                if (DisableExpandedShiny || !(path.Contains("/991.") || path.Contains("/7.")))
                    return new(HttpStatusCode.NotFound);
            }
            if (SlowDex != 0 && path.EndsWith($"/shiny/{SlowDex}.json")) await Task.Delay(5500, cancellationToken);
            if (path.Contains("/27.")) return new(HttpStatusCode.NotFound);
            if (path.Contains("/26.") && !path.EndsWith("/sprites/pokemon/shiny/26.png")) throw new TaskCanceledException("fixture timeout");
            if (path.Contains("/25.") && !path.EndsWith("/sprites/pokemon/shiny/25.png")) return new(HttpStatusCode.NotFound);
            var shiny = path.Contains("/shiny/");
            byte[] content;
            if (path.EndsWith(".json"))
            {
                var frames = new List<object>();
                object Frame(string name, int x, int y, int w, int h) => new
                {
                    filename = name, frame = new { x, y, w, h }, sourceSize = new { w, h },
                    spriteSourceSize = new { x = 0, y = 0, w, h }
                };
                if (path.Contains("pokemon_icons"))
                    for (var i = 1; i <= 151; i++) { frames.Add(Frame($"{i}.png", 0, 0, 2, 2)); frames.Add(Frame($"{i}s.png", 100, 0, 2, 2)); }
                else if (path.Contains("egg/egg"))
                    foreach (var name in new[] { "egg_0", "egg_1", "egg_2", "egg_3", "egg_manaphy" }) frames.Add(Frame(name, 0, 0, name == "egg_manaphy" ? 26 : 28, name == "egg_manaphy" ? 31 : 30));
                else
                {
                    frames.Add(Frame("0001.png", 0, 0, 100, 80));
                    if (!path.Contains("/991.") || path.Contains("/exp/shiny/"))
                        frames.Add(Frame("0002.png", 100, 0, 100, 80));
                }
                content = JsonSerializer.SerializeToUtf8Bytes(new { textures = new[] { new { frames } } });
            }
            else if (path.EndsWith(".png"))
            {
                var pixels = new byte[200 * 80 * 4];
                for (var i = 0; i < 200 * 80; i++) { pixels[i * 4 + ((shiny || i % 200 >= 100) ? 0 : 2)] = 220; pixels[i * 4 + 3] = 255; }
                var bitmap = BitmapSource.Create(200, 80, 96, 96, PixelFormats.Bgra32, null, pixels, 800);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = new MemoryStream(); png.Save(stream); content = stream.ToArray();
            }
            else return new(HttpStatusCode.NotFound);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
        }
    }
}
