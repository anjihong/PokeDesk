using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeskPokemon;

public sealed class PokemonProgress
{
    public int Level { get; set; } = 1;
    public int Exp { get; set; }
}

/// <summary>%LOCALAPPDATA%\DeskPokemon\settings.json — 선택 포켓몬 + 포켓몬별 레벨/경험치 + 도감 보유 + 알.</summary>
public sealed class Settings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskPokemon", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private const int Starter = 4;        // 파이리
    private const int CurrentSchema = 1;  // 1: 알/도감 도입

    /// <summary>실행 시간 기준 알 지급 간격(초).</summary>
    public const int EggIntervalSeconds = 30 * 60;

    public int SchemaVersion { get; set; }              // 0 = 알 기능 이전 파일
    public int SelectedDex { get; set; } = Starter;
    public Dictionary<int, PokemonProgress> Progress { get; set; } = new();
    public HashSet<int> Owned { get; set; } = new();    // 도감에 등록된(부화한) 종
    public int Eggs { get; set; }                       // 보유 알
    public double EggSeconds { get; set; }              // 다음 알까지 누적 실행 시간(초)

    /// <summary>다음 레벨까지 필요한 입력 횟수. 레벨에 비례.</summary>
    public static int ExpToNext(int level) => level * 10;

    public PokemonProgress For(int dex)
    {
        if (!Progress.TryGetValue(dex, out var p))
            Progress[dex] = p = new PokemonProgress();
        return p;
    }

    /// <summary>입력 1회 = Exp +1. 레벨업했으면 true.</summary>
    public bool AddExp(int dex)
    {
        var p = For(dex);
        p.Exp++;
        if (p.Exp < ExpToNext(p.Level)) return false;
        p.Exp -= ExpToNext(p.Level);
        p.Level++;
        return true;
    }

    // ---- 도감 / 알 ----

    public bool IsOwned(int dex) => Owned.Contains(dex);

    /// <summary>도감 등록. 이미 있으면 레벨 +1(Exp는 유지)하고 false.</summary>
    public bool AddOwned(int dex)
    {
        if (Owned.Add(dex)) return true;
        For(dex).Level++;
        return false;
    }

    /// <summary>실행 시간 누적. 간격 채우면 알 1개. 알이 이미 있으면 깔 때까지 일시정지. 지급됐으면 true.</summary>
    public bool TickEgg(double seconds)
    {
        if (Eggs > 0) return false;
        EggSeconds += seconds;
        if (EggSeconds < EggIntervalSeconds) return false;
        EggSeconds = 0;
        Eggs = 1;
        return true;
    }

    [JsonIgnore]
    public int RemainingEggSeconds => (int)Math.Ceiling(EggIntervalSeconds - EggSeconds);

    /// <summary>알 1개 소비해 부화. 알이 없으면 null.</summary>
    public HatchResult? Hatch()
    {
        if (Eggs <= 0) return null;
        Eggs--;
        var dex = EggHatcher.Roll();
        var isNew = AddOwned(dex);
        return new HatchResult(dex, isNew, For(dex).Level);
    }

    /// <summary>구버전 파일 보정. 알 도입 시 보유 목록은 파이리만으로 초기화(레벨 기록은 유지).</summary>
    private void Migrate()
    {
        if (SchemaVersion < 1)
        {
            Owned = new HashSet<int> { Starter };
            SchemaVersion = CurrentSchema;
        }
        Owned.Add(Starter); // 안전장치: 최소 1종은 항상 보유
        if (!Owned.Contains(SelectedDex)) SelectedDex = Starter;
    }

    public static Settings Load()
    {
        Settings s;
        try
        {
            s = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings()
                : new Settings();
        }
        catch
        {
            // 깨진 파일은 기본값으로 시작
            s = new Settings();
        }
        s.Migrate();
        return s;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
