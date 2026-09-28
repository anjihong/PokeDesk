using Microsoft.Win32;
using System.Runtime.Versioning;

namespace DeskPokemon.Platform;

internal interface IWindowsStartupStore
{
    string? Read();
    void Write(string command);
    void Delete();
}

/// <summary>현재 사용자 Run 값 하나만 관리하며 관리자 권한이나 프로세스 실행이 필요하지 않다.</summary>
internal sealed class WindowsStartupRegistration(
    IWindowsStartupStore store, Func<string?> executable, Func<string, bool>? fileExists = null)
    : StartupRegistrationBase(executable, fileExists)
{
    protected override bool IsRegistered() => !string.IsNullOrWhiteSpace(store.Read());
    protected override bool IsAbsolutePath(string path) =>
        path.Length > 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/') ||
        path.StartsWith(@"\\", StringComparison.Ordinal) && path.Length > 4;

    protected override void Register(string executablePath)
    {
        // Windows 파일 이름에는 따옴표를 넣을 수 없다. 명령 셸을 거치지 않고 경로 하나만 인용한다.
        if (executablePath.Contains('"')) throw new InvalidOperationException("실행 파일 경로에 잘못된 따옴표가 있습니다.");
        var command = $"\"{executablePath}\"";
        if (command.Length > 260) throw new InvalidOperationException("Windows 자동 시작 경로는 260자를 넘을 수 없습니다.");
        store.Write(command);
    }

    protected override void Unregister() => store.Delete();
}

[SupportedOSPlatform("windows")]
internal sealed class CurrentUserRunStartupStore : IWindowsStartupStore
{
    internal const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "PokeDesk";

    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
        return key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void Write(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true)
            ?? throw new IOException("현재 사용자 자동 시작 레지스트리를 열 수 없습니다.");
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    public void Delete()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
