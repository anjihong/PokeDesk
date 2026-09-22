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
    private static readonly string FilePath = AppPaths.SettingsFile;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>1~9세대 스타팅(풀·불꽃·물).</summary>
    private static readonly int[] GrassStarters = [1, 152, 252, 387, 495, 650, 722, 810, 906];
    private static readonly int[] FireStarters = [4, 155, 255, 390, 498, 653, 725, 813, 909];
    private static readonly int[] WaterStarters = [7, 158, 258, 393, 501, 656, 728, 816, 912];

    /// <summary>스타팅 후보: 풀·불꽃·물 각각 1~9세대 중 무작위 1종.</summary>
    public static int[] RollStarterChoices() =>
        [.. new[] { GrassStarters, FireStarters, WaterStarters }.Select(t => t[Random.Shared.Next(t.Length)])];
    private const int LegacyStarter = 4;  // 파이리: 스타팅 도입 이전 세이브의 고정 스타팅
    private const int CurrentSchema = 1;  // 1: 알/도감 도입

    /// <summary>실행 시간 기준 알 지급 간격(초).</summary>
    public const int EggIntervalSeconds = 30 * 60;

    public int SchemaVersion { get; set; }              // 0 = 알 기능 이전 파일
    public int StarterDex { get; set; }                 // 0 = 스타팅 도입 이전 파일
    public int SelectedDex { get; set; }
    public Dictionary<int, PokemonProgress> Progress { get; set; } = new();
    public HashSet<int> Owned { get; set; } = new();    // 도감에 등록된(부화한) 종
    public int Eggs { get; set; }                       // 보유 알
    public double EggSeconds { get; set; }              // 다음 알까지 누적 실행 시간(초)

    /// <summary>다음 레벨까지 필요한 입력 횟수: 현재 레벨 × 30.</summary>
    public static int ExpToNext(int level) => level * 30;

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

    /// <summary>Debug 치트: 전체 해금. 저장 안 함 — 끄면 원래 Owned로 돌아감.</summary>
    [JsonIgnore] public bool UnlockAll { get; set; }

    public bool IsOwned(int dex) => UnlockAll || Owned.Contains(dex);

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

    /// <summary>구버전 파일 보정. 기존 세이브의 스타팅은 파이리. 알 도입 시 보유 목록은 스타팅만으로 초기화(레벨 기록은 유지).</summary>
    private void Migrate()
    {
        if (StarterDex == 0) StarterDex = LegacyStarter;
        if (SchemaVersion < 1)
        {
            Owned = new HashSet<int> { StarterDex };
            SchemaVersion = CurrentSchema;
        }
        Owned.Add(StarterDex); // 안전장치: 최소 1종은 항상 보유
        if (!Owned.Contains(SelectedDex)) SelectedDex = StarterDex;
    }

    /// <summary>세이브 로드. 없거나 깨졌으면 null → 스타팅 선택부터 새로 시작.</summary>
    public static Settings? Load()
    {
        Settings? s = null;
        try
        {
            if (File.Exists(FilePath))
                s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
        }
        catch
        {
            // 깨진 파일은 새로 시작
        }
        s?.Migrate();
        return s;
    }

    /// <summary>고른 스타팅으로 새 세이브. 스타팅만 보유·선택.</summary>
    public static Settings New(int starterDex)
    {
        var s = new Settings { StarterDex = starterDex };
        s.Migrate();
        return s;
    }

    /// <summary>세이브 파일 삭제(초기화).</summary>
    public static void Delete() => File.Delete(FilePath);

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
