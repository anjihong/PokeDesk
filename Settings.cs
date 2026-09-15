using System.IO;
using System.Text.Json;

namespace DeskPokemon;

public sealed class PokemonProgress
{
    public int Level { get; set; } = 1;
    public int Exp { get; set; }
}

/// <summary>%LOCALAPPDATA%\DeskPokemon\settings.json — 선택 포켓몬 + 포켓몬별 레벨/경험치.</summary>
public sealed class Settings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskPokemon", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int SelectedDex { get; set; } = 4; // 파이리
    public Dictionary<int, PokemonProgress> Progress { get; set; } = new();

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

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch
        {
            // 깨진 파일은 기본값으로 시작
        }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
