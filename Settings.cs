using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeskPokemon;

public sealed class PokemonProgress
{
    public int Level { get; set; } = 1;
    public int Exp { get; set; }
}

/// <summary>색상별 보유·성장 및 생성 시 확정된 알 결과를 저장한다.</summary>
public sealed class Settings
{
    private static readonly string FilePath = AppPaths.SettingsFile;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private string? savePath = FilePath; // null means an in-memory preview; Save must not touch disk.
    private static readonly int[] GrassStarters = [1, 152, 252, 387, 495, 650, 722, 810, 906];
    private static readonly int[] FireStarters = [4, 155, 255, 390, 498, 653, 725, 813, 909];
    private static readonly int[] WaterStarters = [7, 158, 258, 393, 501, 656, 728, 816, 912];
    public static int[] RollStarterChoices() =>
        [.. new[] { GrassStarters, FireStarters, WaterStarters }.Select(t => t[Random.Shared.Next(t.Length)])];
    private const int LegacyStarter = 4;
    private const int CurrentSchema = 2;
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
    public HashSet<int> ShinyOwned { get; set; } = new();
    public int Eggs { get; set; }
    public double EggSeconds { get; set; }
    public PendingEgg? PendingEgg { get; set; }

    public static int ExpToNext(int level) => level * 30;
    public PokemonProgress For(int dex, bool shiny = false)
    {
        var progress = shiny ? ShinyProgress : Progress;
        if (!progress.TryGetValue(dex, out var p)) progress[dex] = p = new PokemonProgress();
        return p;
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
    public bool IsOwned(int dex, bool shiny = false) => UnlockAll || (shiny ? ShinyOwned : Owned).Contains(dex);
    public bool AddOwned(int dex, bool shiny = false)
    {
        if ((shiny ? ShinyOwned : Owned).Add(dex)) return true;
        For(dex, shiny).Level++;
        return false;
    }

    /// <summary>Only actual same-color ownership qualifies, including growth earned on an older form.</summary>
    public int EffectiveEvolutionLevel(int dex, bool shiny = false) => EvolutionProgress(dex, shiny).Level;

    public IReadOnlyList<EvolutionOption> AvailableEvolutions(int dex, bool shiny = false)
    {
        var owned = shiny ? ShinyOwned : Owned;
        if (!owned.Contains(dex)) return Array.Empty<EvolutionOption>();
        var level = EffectiveEvolutionLevel(dex, shiny);
        return EvolutionRules.OptionsFor(dex).Where(option => option.RequiredLevel <= level && !owned.Contains(option.TargetDex)).ToArray();
    }

    public bool CanEvolve(int dex, bool shiny = false) => AvailableEvolutions(dex, shiny).Count > 0;

    private (int Level, int Exp) EvolutionProgress(int dex, bool shiny)
    {
        var owned = shiny ? ShinyOwned : Owned;
        var progress = shiny ? ShinyProgress : Progress;
        var strongest = (Level: 1, Exp: 0);
        foreach (var ancestor in EvolutionRules.SelfAndAncestors(dex))
        {
            if (!owned.Contains(ancestor) || !progress.TryGetValue(ancestor, out var value)) continue;
            if (value.Level > strongest.Level || value.Level == strongest.Level && value.Exp > strongest.Exp)
                strongest = (value.Level, value.Exp);
        }
        return strongest;
    }

    /// <summary>Adds one unowned direct evolution, preserves prior forms, and saves selection/ownership atomically.</summary>
    public EvolutionResult? Evolve(int dex, int target, bool shiny = false)
    {
        if (!AvailableEvolutions(dex, shiny).Any(option => option.TargetDex == target)) return null;
        var owned = shiny ? ShinyOwned : Owned;
        var progress = shiny ? ShinyProgress : Progress;
        var inherited = EvolutionProgress(dex, shiny);
        var hadProgress = progress.TryGetValue(target, out var previousProgress);
        // A recoverable orphan record must never lose stronger growth when ownership is restored.
        if (previousProgress is not null && (previousProgress.Level > inherited.Level ||
            previousProgress.Level == inherited.Level && previousProgress.Exp > inherited.Exp))
            inherited = (previousProgress.Level, previousProgress.Exp);
        var previousDex = SelectedDex;
        var previousShiny = SelectedShiny;
        try
        {
            owned.Add(target);
            progress[target] = new PokemonProgress { Level = inherited.Level, Exp = inherited.Exp };
            if (SelectedDex == dex && SelectedShiny == shiny) SelectedDex = target;
            Save();
            return new EvolutionResult(dex, target, shiny, inherited.Level, inherited.Exp);
        }
        catch
        {
            owned.Remove(target);
            if (hadProgress) progress[target] = previousProgress!;
            else progress.Remove(target);
            SelectedDex = previousDex;
            SelectedShiny = previousShiny;
            throw;
        }
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
        var alreadyOwned = owned.Contains(egg.Dex);
        var hadProgress = progress.TryGetValue(egg.Dex, out var oldProgress);
        var oldLevel = oldProgress?.Level ?? 1;
        var oldEggs = Eggs;
        var oldSeconds = EggSeconds;
        try
        {
            var isNew = AddOwned(egg.Dex, egg.IsShiny);
            var result = new HatchResult(egg.Dex, egg.IsShiny, isNew, For(egg.Dex, egg.IsShiny).Level);
            Eggs = 0;
            EggSeconds = 0;
            PendingEgg = EggHatcher.Create();
            Save();
            return result;
        }
        catch
        {
            if (!alreadyOwned) owned.Remove(egg.Dex);
            if (hadProgress) oldProgress!.Level = oldLevel;
            else progress.Remove(egg.Dex);
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
        if (SchemaVersion < 1) Owned = new HashSet<int> { StarterDex };
        if (SchemaVersion < 2)
        {
            SelectedShiny = false;
            PendingEgg = EggHatcher.Create(Eggs > 0 ? EggKind.Common : null);
        }
        if (PendingEgg is null || !Enum.IsDefined(PendingEgg.Kind) || !BaseSpecies.Dex.Contains(PendingEgg.Dex))
        {
            PendingEgg = EggHatcher.Create(Eggs > 0 ? EggKind.Common : null);
            changed = true;
        }
        if (Owned.Add(StarterDex)) changed = true;
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
        try { s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)); }
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
    /// <summary>Creates a fully initialized preview without reading or writing a user save.</summary>
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
