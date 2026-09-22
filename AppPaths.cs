namespace DeskPokemon;

/// <summary>Windows keeps its existing save location; macOS uses Application Support.</summary>
public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeskPokemon");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string SpriteCache => Path.Combine(DataDirectory, "sprites");
}
