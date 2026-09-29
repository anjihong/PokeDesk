using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeskPokemon;

public sealed class PokemonProgress
{
    public int Level { get; set; } = 1;
    public int Exp { get; set; }
    public int CurrentDex { get; set; }
    public List<int> History { get; set; } = new();
    public int? PendingEvolution { get; set; }
}

/// <summary>색상별 보유·성장 및 생성 시 확정된 알 결과를 저장한다.</summary>
public sealed class Settings
{
    private static readonly string FilePath = AppPaths.SettingsFile;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private string? savePath = FilePath; // null means a preview; Save must never touch disk.
    private static readonly HashSet<int> LegacyEeveeEggs = [134, 135, 136, 196, 197, 470, 471, 700];
    private static bool IsValidPendingEgg(int dex) =>
        EvolutionData.EggPool.Contains(dex) || LegacyEeveeEggs.Contains(dex);
    private static readonly int[] GrassStarters = [1, 152, 252, 387, 495, 650, 722, 810, 906];
    private static readonly int[] FireStarters = [4, 155, 255, 390, 498, 653, 725, 813, 909];
    private static readonly int[] WaterStarters = [7, 158, 258, 393, 501, 656, 728, 816, 912];
    public static int[] RollStarterChoices() =>
        [.. new[] { GrassStarters, FireStarters, WaterStarters }.Select(t => t[Random.Shared.Next(t.Length)])];
    private const int LegacyStarter = 4;
    private const int CurrentSchema = 3;
    public const int EggIntervalSeconds = 30 * 60;

    public int SchemaVersion { get; set; }
    public int StarterDex { get; set; }
    public int SelectedDex { get; set; }
    public bool SelectedShiny { get; set; }
    public bool FlipHorizontal { get; set; }
    public int UiScale { get; set; } = 2;
    public Dictionary<int, PokemonProgress> Progress { get; set; } = new();
    public HashSet<int> Owned { get; set; } = new();
    public Dictionary<int, PokemonProgress> ShinyProgress { get; set; } = new();
    public Dictionary<int, int> GrowthLinks { get; set; } = new();
    public Dictionary<int, int> ShinyGrowthLinks { get; set; } = new();
    public HashSet<int> ShinyOwned { get; set; } = new();
    public int Eggs { get; set; }
    public double EggSeconds { get; set; }
    public PendingEgg? PendingEgg { get; set; }

    public static int ExpToNext(int level) => level * 30;
    public PokemonProgress For(int dex, bool shiny = false)
    {
        if (!EvolutionData.Contains(dex)) throw new ArgumentOutOfRangeException(nameof(dex));
        var progress = shiny ? ShinyProgress : Progress;
        var links = shiny ? ShinyGrowthLinks : GrowthLinks;
        if (links.TryGetValue(dex, out var key)) return progress[key];
        return StartGrowth(dex, shiny);
    }

    private PokemonProgress StartGrowth(int dex, bool shiny)
    {
        var records = shiny ? ShinyProgress : Progress;
        var links = shiny ? ShinyGrowthLinks : GrowthLinks;
        var key = records.Keys.DefaultIfEmpty(0).Max() + 1;
        links[dex] = key;
        return records[key] = new PokemonProgress { CurrentDex = dex, History = new() { dex } };
    }

    public bool HasOwned(int dex, bool shiny = false) => (shiny ? ShinyOwned : Owned).Contains(dex);

    public EvolutionRule[] EvolutionOptions(int dex, bool shiny = false)
    {
        if (!HasOwned(dex, shiny)) return [];
        var p = For(dex, shiny);
        if (!HasOwned(p.CurrentDex, shiny) || !ReferenceEquals(p, For(p.CurrentDex, shiny))) return [];
        var rules = EvolutionData.From(p.CurrentDex);
        if (rules.Length > 1) rules = rules.Where(r => !HasOwned(r.ToId, shiny)).ToArray();
        return rules.Where(r => r.Level <= p.Level && !p.History.Contains(r.ToId)).ToArray();
    }

    /// <summary>모든 분기 진화의 무작위 결과를 먼저 저장하여 이미지 실패·재시작 때 재추첨하지 않는다.</summary>
    public int PrepareEvolution(int dex, bool shiny = false, int? target = null, Random? random = null)
    {
        var p = For(dex, shiny);
        var options = EvolutionOptions(dex, shiny);
        if (p.CurrentDex != dex || options.Length == 0) throw new InvalidOperationException("Evolution is not ready.");
        if (p.PendingEvolution is { } pending)
        {
            if (!options.Any(r => r.ToId == pending)) throw new InvalidOperationException("Invalid pending evolution.");
            return pending;
        }
        var chosen = target ?? options[(random ?? Random.Shared).Next(options.Length)].ToId;
        if (!options.Any(r => r.ToId == chosen)) throw new ArgumentOutOfRangeException(nameof(target));
        p.PendingEvolution = chosen;
        try { Save(); }
        catch { p.PendingEvolution = null; throw; }
        return chosen;
    }

    /// <summary>이미지 준비 후 모델과 선택을 함께 저장한다. 실패하면 성장·보유·연결·선택을 복원한다.</summary>
    public void CompleteEvolution(int dex, bool shiny, int target)
    {
        var p = For(dex, shiny);
        if (p.CurrentDex != dex || p.PendingEvolution != target ||
            !EvolutionOptions(dex, shiny).Any(r => r.ToId == target))
            throw new InvalidOperationException("Evolution is no longer ready.");
        var links = shiny ? ShinyGrowthLinks : GrowthLinks;
        var owned = shiny ? ShinyOwned : Owned;
        var oldLink = links.TryGetValue(target, out var previous) ? previous : (int?)null;
        var wasOwned = owned.Contains(target);
        var oldDex = SelectedDex;
        var oldShiny = SelectedShiny;
        try
        {
            links[target] = links[dex];
            owned.Add(target);
            p.CurrentDex = target;
            p.History.Add(target);
            p.PendingEvolution = null;
            SelectedDex = target;
            SelectedShiny = shiny;
            Save();
        }
        catch
        {
            if (oldLink is { } key) links[target] = key; else links.Remove(target);
            if (!wasOwned) owned.Remove(target);
            p.CurrentDex = dex;
            p.History.RemoveAt(p.History.Count - 1);
            p.PendingEvolution = target;
            SelectedDex = oldDex;
            SelectedShiny = oldShiny;
            throw;
        }
    }

    /// <summary>Direct eligible options for the current appearance. Earlier appearances use EvolutionOptions for their notice.</summary>
    public IReadOnlyList<EvolutionOption> AvailableEvolutions(int dex, bool shiny = false) =>
        HasOwned(dex, shiny) && For(dex, shiny).CurrentDex == dex
            ? EvolutionOptions(dex, shiny).Select(rule => new EvolutionOption(rule.ToId, rule.Level)).ToArray()
            : Array.Empty<EvolutionOption>();

    public int EffectiveEvolutionLevel(int dex, bool shiny = false) => For(dex, shiny).Level;
    public bool CanEvolve(int dex, bool shiny = false) => AvailableEvolutions(dex, shiny).Count > 0;

    /// <summary>Compatibility helper: prepare and commit one direct step. Production UI prepares before loading artwork.</summary>
    public EvolutionResult? Evolve(int dex, int target, bool shiny = false)
    {
        if (!AvailableEvolutions(dex, shiny).Any(option => option.TargetDex == target)) return null;
        if (For(dex, shiny).PendingEvolution is { } pending && pending != target) return null;
        PrepareEvolution(dex, shiny, target);
        CompleteEvolution(dex, shiny, target);
        var p = For(target, shiny);
        return new EvolutionResult(dex, target, shiny, p.Level, p.Exp);
    }

    private bool CanRestart(int dex, bool shiny)
    {
        if (!EvolutionData.EggPool.Contains(dex)) return false;
        var p = For(dex, shiny);
        return p.History.Take(p.History.Count - 1).Any(from => EvolutionData.IsBranch(from) &&
            EvolutionData.From(from).Any(r => !HasOwned(r.ToId, shiny)));
    }

    public bool AddExp(int dex, bool shiny = false)
    {
        var p = For(dex, shiny);
        p.Exp++;
        if (p.Exp < ExpToNext(p.Level)) return false;
        p.Exp -= ExpToNext(p.Level);
        p.Level++;
        return true;
    }

    [JsonIgnore] public bool UnlockAll { get; set; }
    public bool IsOwned(int dex, bool shiny = false) => EvolutionData.Contains(dex) && (UnlockAll || HasOwned(dex, shiny));
    public bool AddOwned(int dex, bool shiny = false)
    {
        if (!EvolutionData.Contains(dex)) throw new ArgumentOutOfRangeException(nameof(dex));
        if ((shiny ? ShinyOwned : Owned).Add(dex)) { StartGrowth(dex, shiny); return true; }
        if (CanRestart(dex, shiny)) { StartGrowth(dex, shiny); return false; }
        For(dex, shiny).Level++;
        return false;
    }

    public bool TickEgg(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Eggs > 0) return false;
        EggSeconds += seconds;
        if (EggSeconds < EggIntervalSeconds) return false;
        EggSeconds = 0;
        Eggs = 1;
        return true;
    }

    [JsonIgnore] public int RemainingEggSeconds => Eggs > 0 ? 0 : (int)Math.Ceiling(EggIntervalSeconds - EggSeconds);

    /// <summary>확정 결과 지급과 다음 알 생성을 하나의 파일 교체로 저장한다. 실패하면 메모리도 복구한다.</summary>
    public HatchResult? Hatch()
    {
        if (Eggs <= 0) return null;
        var egg = PendingEgg ?? throw new InvalidOperationException("Missing pending egg result.");
        var owned = egg.IsShiny ? ShinyOwned : Owned;
        var progress = egg.IsShiny ? ShinyProgress : Progress;
        var links = egg.IsShiny ? ShinyGrowthLinks : GrowthLinks;
        if (!IsValidPendingEgg(egg.Dex)) throw new InvalidOperationException("Invalid egg species.");
        var alreadyOwned = owned.Contains(egg.Dex);
        var oldLink = links.TryGetValue(egg.Dex, out var linked) ? linked : (int?)null;
        var oldProgress = oldLink is { } oldKey ? progress[oldKey] : null;
        var restart = alreadyOwned && CanRestart(egg.Dex, egg.IsShiny);
        var oldLevel = oldProgress?.Level ?? 1;
        var oldEggs = Eggs;
        var oldSeconds = EggSeconds;
        try
        {
            var isNew = AddOwned(egg.Dex, egg.IsShiny);
            var result = new HatchResult(egg.Dex, egg.IsShiny, isNew, For(egg.Dex, egg.IsShiny).Level, restart);
            Eggs = 0;
            EggSeconds = 0;
            PendingEgg = EggHatcher.Create();
            Save();
            return result;
        }
        catch
        {
            if (!alreadyOwned) owned.Remove(egg.Dex);
            if (links.TryGetValue(egg.Dex, out var current) && current != oldLink) progress.Remove(current);
            if (oldLink is { } key) { links[egg.Dex] = key; oldProgress!.Level = oldLevel; }
            else links.Remove(egg.Dex);
            Eggs = oldEggs;
            EggSeconds = oldSeconds;
            PendingEgg = egg;
            throw;
        }
    }

#if DEBUG
    public void GrantTestEgg(EggKind kind, bool forceShiny)
    {
        PendingEgg = EggHatcher.Create(kind, forceShiny);
        Eggs = 1;
        EggSeconds = 0;
    }

    internal void GrantTestEgg(int dex, bool shiny)
    {
        if (!EvolutionData.EggPool.Contains(dex)) throw new ArgumentOutOfRangeException(nameof(dex));
        var previous = (PendingEgg, Eggs, EggSeconds);
        PendingEgg = new PendingEgg(EggKind.Common, dex, shiny);
        Eggs = 1;
        EggSeconds = 0;
        try { Save(); }
        catch
        {
            (PendingEgg, Eggs, EggSeconds) = previous;
            throw;
        }
    }

    internal int? NextTestEvolutionLevel(int dex, bool shiny)
    {
        if (!HasOwned(dex, shiny)) return null;
        var p = For(dex, shiny);
        var rules = EvolutionData.From(p.CurrentDex);
        if (rules.Length > 1) rules = rules.Where(r => !HasOwned(r.ToId, shiny)).ToArray();
        return rules.Where(r => !p.History.Contains(r.ToId)).Select(r => (int?)r.Level).Min();
    }

    internal int? JumpToNextTestEvolutionLevel(int dex, bool shiny)
    {
        var target = NextTestEvolutionLevel(dex, shiny);
        if (target is null) return null;
        var p = For(dex, shiny);
        var previous = p.Level;
        p.Level = Math.Max(p.Level, target.Value);
        try { Save(); }
        catch { p.Level = previous; throw; }
        return p.Level;
    }
#endif

    private bool Migrate()
    {
        var changed = SchemaVersion < CurrentSchema;
        if (StarterDex <= 0 || StarterDex > 1025) { StarterDex = LegacyStarter; changed = true; }
        if (Owned is null || Progress is null || ShinyOwned is null || ShinyProgress is null) changed = true;
        Owned ??= new();
        Progress ??= new();
        ShinyOwned ??= new();
        ShinyProgress ??= new();
        if (GrowthLinks is null || ShinyGrowthLinks is null) changed = true;
        GrowthLinks ??= new();
        ShinyGrowthLinks ??= new();
        if (SchemaVersion < 1) Owned = new HashSet<int> { StarterDex };
        if (SchemaVersion < 2)
        {
            SelectedShiny = false;
            PendingEgg = EggHatcher.Create(Eggs > 0 ? EggKind.Common : null);
        }
        if (PendingEgg is null || !Enum.IsDefined(PendingEgg.Kind) || !IsValidPendingEgg(PendingEgg.Dex))
        {
            PendingEgg = EggHatcher.Create(Eggs > 0 ? EggKind.Common : null);
            changed = true;
        }
        if (Owned.Add(StarterDex)) changed = true;
        if (SchemaVersion < 3)
        {
            foreach (var shiny in new[] { false, true })
            {
                var records = shiny ? ShinyProgress : Progress;
                var links = shiny ? ShinyGrowthLinks : GrowthLinks;
                links.Clear();
                foreach (var (dex, p) in records)
                {
                    if (!EvolutionData.Contains(dex)) continue;
                    if (p is null) throw new InvalidDataException($"Invalid growth record for Pokémon {dex}.");
                    p.CurrentDex = dex;
                    p.History = new() { dex };
                    p.PendingEvolution = null;
                    links[dex] = dex;
                }
            }
        }
        foreach (var shiny in new[] { false, true })
        {
            foreach (var dex in (shiny ? ShinyOwned : Owned))
                if (EvolutionData.Contains(dex) && !(shiny ? ShinyGrowthLinks : GrowthLinks).ContainsKey(dex))
                {
                    if (SchemaVersion >= 3)
                        throw new InvalidDataException($"Missing growth link for Pokémon {dex}.");
                    For(dex, shiny);
                    changed = true;
                }
            var records = shiny ? ShinyProgress : Progress;
            foreach (var (dex, key) in (shiny ? ShinyGrowthLinks : GrowthLinks))
            {
                if (!EvolutionData.Contains(dex) || !records.TryGetValue(key, out var p) || p is null ||
                    p.Level < 1 || p.Exp < 0 || p.History is null || !p.History.Contains(dex) ||
                    !EvolutionData.Contains(p.CurrentDex) || p.History.LastOrDefault() != p.CurrentDex ||
                    (p.PendingEvolution is { } target && !EvolutionData.From(p.CurrentDex).Any(r => r.ToId == target && r.Level <= p.Level)))
                    throw new InvalidDataException($"Invalid growth record for Pokémon {dex}.");
            }
        }
        if (!(SelectedShiny ? ShinyOwned : Owned).Contains(SelectedDex))
        {
            SelectedDex = StarterDex;
            SelectedShiny = false;
            changed = true;
        }
        if (UiScale is not (2 or 4 or 6 or 8)) { UiScale = 2; changed = true; }
        var eggs = Math.Clamp(Eggs, 0, 1);
        var seconds = double.IsFinite(EggSeconds) ? Math.Clamp(EggSeconds, 0, EggIntervalSeconds) : 0;
        if (Eggs != eggs || EggSeconds != seconds) changed = true;
        Eggs = eggs;
        EggSeconds = seconds;
        SchemaVersion = CurrentSchema;
        return changed;
    }

    public static Settings? Load() => LoadFrom(FilePath);

    // Explicit path keeps tests entirely separate from the user's save.
    internal static Settings? LoadFrom(string path)
    {
        if (!File.Exists(path)) return null;
        Settings? s;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            s = document.RootElement.Deserialize<Settings>();
            if (s?.SchemaVersion == 3)
            {
                // Schema 3 owns explicit run links. Missing collections are corruption,
                // not legacy data: inventing links would silently replace visible growth.
                foreach (var (name, kind) in new[]
                {
                    (nameof(Progress), JsonValueKind.Object), (nameof(ShinyProgress), JsonValueKind.Object),
                    (nameof(GrowthLinks), JsonValueKind.Object), (nameof(ShinyGrowthLinks), JsonValueKind.Object),
                    (nameof(Owned), JsonValueKind.Array), (nameof(ShinyOwned), JsonValueKind.Array)
                })
                    if (!document.RootElement.TryGetProperty(name, out var value) || value.ValueKind != kind)
                        throw new InvalidDataException($"Missing or invalid schema 3 collection: {name}.");
            }
        }
        catch (JsonException ex) { throw new InvalidDataException("Save data is not a valid settings object.", ex); }
        if (s is null) throw new InvalidDataException("Save data must contain a settings object.");
        if (s.SchemaVersion > CurrentSchema)
            throw new InvalidDataException($"Save schema {s.SchemaVersion} requires a newer version of DeskPokemon.");
        s.savePath = path;
        var oldSchema = s.SchemaVersion;
        if (s.Migrate())
        {
            if (oldSchema < CurrentSchema && !File.Exists(path + $".schema{oldSchema}.bak"))
                File.Copy(path, path + $".schema{oldSchema}.bak", overwrite: false);
            s.Save();
        }
        return s;
    }

    public static Settings New(int starterDex) => NewAt(starterDex, FilePath);
    /// <summary>Creates initialized in-memory settings without reading or writing a user save.</summary>
    internal static Settings NewPreview(int starterDex)
    {
        var s = new Settings { StarterDex = starterDex, savePath = null };
        s.Migrate();
        return s;
    }

    internal static Settings NewAt(int starterDex, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var s = new Settings { StarterDex = starterDex, savePath = path };
        s.Migrate();
        s.Save();
        return s;
    }
    public static void Delete() => File.Delete(FilePath);

    public void Save()
    {
        if (savePath is null) return;
        var path = Path.GetFullPath(savePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, this, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
