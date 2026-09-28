using DeskPokemon.Platform;

namespace DeskPokemon;

/// <summary>OS 등록 상태를 조회하며 사용자가 설정을 바꿀 때만 등록을 변경한다.</summary>
public interface IStartupRegistration
{
    StartupRegistrationResult Query();
    StartupRegistrationResult SetEnabled(bool enabled);
}

public readonly record struct StartupRegistrationResult(bool IsSupported, bool IsEnabled, string? Error = null);

public static class StartupRegistration
{
    /// <summary>생성만으로 레지스트리나 LaunchAgents 파일을 변경하지 않는다.</summary>
    public static IStartupRegistration CreateDefault()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsStartupRegistration(new CurrentUserRunStartupStore(), () => Environment.ProcessPath);
        if (OperatingSystem.IsMacOS())
            return new MacStartupRegistration(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents"),
                () => Environment.ProcessPath);
        return new UnsupportedStartupRegistration();
    }
}

internal abstract class StartupRegistrationBase(Func<string?> executable, Func<string, bool>? fileExists = null) : IStartupRegistration
{
    protected abstract bool IsRegistered();
    protected abstract void Register(string executablePath);
    protected abstract void Unregister();
    protected abstract bool IsAbsolutePath(string path);

    public StartupRegistrationResult Query()
    {
        try { return new(true, IsRegistered()); }
        catch (Exception ex) { return new(true, false, $"자동 시작 등록을 확인하지 못했습니다: {ex.Message}"); }
    }

    public StartupRegistrationResult SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                var path = executable();
                if (string.IsNullOrWhiteSpace(path) || !IsAbsolutePath(path) || path.Any(char.IsControl))
                    throw new InvalidOperationException("앱 실행 파일의 전체 경로를 확인할 수 없습니다.");
                var name = path.Replace('\\', '/').Split('/')[^1];
                if (name.Equals("dotnet", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("dotnet 개발 실행에서는 등록할 수 없습니다. 배포된 PokeDesk 앱에서 설정해 주세요.");
                if (!(fileExists ?? File.Exists)(path))
                    throw new FileNotFoundException("앱 실행 파일을 찾을 수 없습니다.", path);
                Register(path);
            }
            else Unregister();
            return Query();
        }
        catch (Exception ex)
        {
            return new(true, Query().IsEnabled, $"자동 시작 설정을 변경하지 못했습니다: {ex.Message}");
        }
    }
}

internal sealed class UnsupportedStartupRegistration : IStartupRegistration
{
    public StartupRegistrationResult Query() => new(false, false, "이 운영체제에서는 자동 시작 설정을 지원하지 않습니다.");
    public StartupRegistrationResult SetEnabled(bool enabled) => Query();
}
