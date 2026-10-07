#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $PublishDirectory,
    [string] $ReportPath,
    [switch] $LaunchSmokeTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path -LiteralPath $PublishDirectory -PathType Container)) {
    throw "Publish directory does not exist: $PublishDirectory"
}
$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).ProviderPath
if (-not $ReportPath) { $ReportPath = Join-Path $publishRoot 'windows-validation.json' }
$reportFile = [IO.Path]::GetFullPath($ReportPath)
$reportDirectory = [IO.Path]::GetDirectoryName($reportFile)
[IO.Directory]::CreateDirectory($reportDirectory) | Out-Null

function Read-X64Image([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5A4D) {
            throw "Not a DOS/PE executable: $Path"
        }
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 64 -or $peOffset -gt $stream.Length - 26) {
            throw "Invalid PE header offset: $Path"
        }
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) { throw "Invalid PE signature: $Path" }
        $machine = $reader.ReadUInt16()
        if ($machine -ne 0x8664) {
            throw ('Expected AMD64 PE machine 0x8664, got 0x{0:X4}: {1}' -f $machine, $Path)
        }
        $stream.Position = $peOffset + 24
        if ($reader.ReadUInt16() -ne 0x020B) { throw "Expected a PE32+ optional header: $Path" }
        return [ordered]@{ file = [IO.Path]::GetFileName($Path); machine = 'AMD64'; format = 'PE32+' }
    }
    finally { $reader.Dispose() }
}

function Invoke-StartupSmoke([string] $Executable) {
    if (-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true') {
        throw '-LaunchSmokeTest is restricted to a disposable Windows GitHub Actions runner.'
    }
    $dataRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'DeskPokemon'
    if (Test-Path -LiteralPath $dataRoot) {
        throw "Native startup requires a fresh profile; existing data will not be opened or changed: $dataRoot"
    }

    if (-not ('PokeDesk.PublishValidation.NativeWindow' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
namespace PokeDesk.PublishValidation {
    public static class NativeWindow {
        [return: MarshalAs(UnmanagedType.Bool)]
        private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
        [StructLayout(LayoutKind.Sequential)]
        public struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(IntPtr window, out Rect bounds);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr window);

        public static string Title(IntPtr window) {
            var text = new StringBuilder(1024);
            GetWindowText(window, text, text.Capacity);
            return text.ToString();
        }

        public static IntPtr FindVisibleWindow(int processId, string expectedTitle) {
            var result = IntPtr.Zero;
            // Owned windows are intentional: Avalonia hides taskbar entries with
            // an offscreen owner, so Process.MainWindowHandle can miss them.
            if (!EnumWindows((window, parameter) => {
                GetWindowThreadProcessId(window, out var ownerProcessId);
                if (result == IntPtr.Zero && ownerProcessId == (uint)processId &&
                    IsWindowVisible(window) && String.Equals(Title(window), expectedTitle, StringComparison.Ordinal))
                    result = window;
                return true;
            }, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return result;
        }

        public static void RequestClose(IntPtr window, int processId) {
            GetWindowThreadProcessId(window, out var ownerProcessId);
            if (ownerProcessId != (uint)processId || !IsWindowVisible(window))
                throw new InvalidOperationException("The tested process no longer owns the visible starter window.");
            // A posted WM_CLOSE keeps a hung app from blocking the checker itself.
            if (!PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
'@
    }

    $smokeProcess = $null
    $stdout = Join-Path $reportDirectory 'windows-startup.stdout.log'
    $stderr = Join-Path $reportDirectory 'windows-startup.stderr.log'
    try {
        $smokeProcess = Start-Process -FilePath $Executable -WorkingDirectory $publishRoot -PassThru `
            -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        if (-not $smokeProcess.WaitForInputIdle(15000)) { throw 'The published app did not enter its GUI message loop within 15 seconds.' }
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        $expectedTitle = 'DeskPokemon - 스타팅 선택'
        $window = [IntPtr]::Zero
        do {
            $smokeProcess.Refresh()
            if ($smokeProcess.HasExited) { throw "The published app exited during startup (code $($smokeProcess.ExitCode))." }
            $window = [PokeDesk.PublishValidation.NativeWindow]::FindVisibleWindow($smokeProcess.Id, $expectedTitle)
            if ($window -ne [IntPtr]::Zero) { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::UtcNow -lt $deadline)
        if ($window -eq [IntPtr]::Zero -or -not [PokeDesk.PublishValidation.NativeWindow]::IsWindowVisible($window)) {
            throw 'The published app did not expose its visible native starter window within the startup deadline. An error dialog is not a startup pass.'
        }
        $title = [PokeDesk.PublishValidation.NativeWindow]::Title($window)
        if ($title -ne $expectedTitle) {
            throw "Expected the fresh-profile starter window; got '$title'. An error dialog is not a startup pass."
        }
        $bounds = [PokeDesk.PublishValidation.NativeWindow+Rect]::new()
        if (-not [PokeDesk.PublishValidation.NativeWindow]::GetClientRect($window, [ref]$bounds)) {
            throw 'Could not read the native starter window client bounds.'
        }
        $width = $bounds.Right - $bounds.Left
        $height = $bounds.Bottom - $bounds.Top
        if ($width -lt 200 -or $width -gt 4096 -or $height -lt 60 -or $height -gt 2160) {
            throw "Invalid native starter window size: ${width}x${height}"
        }
        Start-Sleep -Seconds 2
        $smokeProcess.Refresh()
        if ($smokeProcess.HasExited) { throw 'The published app exited before the native startup stability check completed.' }
        $window = [PokeDesk.PublishValidation.NativeWindow]::FindVisibleWindow($smokeProcess.Id, $expectedTitle)
        if ($window -eq [IntPtr]::Zero) { throw 'The native starter window disappeared before the startup stability check completed.' }
        [PokeDesk.PublishValidation.NativeWindow]::RequestClose($window, $smokeProcess.Id)
        if (-not $smokeProcess.WaitForExit(10000)) { throw 'The published app did not terminate within 10 seconds of closing its starter window.' }
        if ($smokeProcess.ExitCode -ne 0) { throw "The published app closed with exit code $($smokeProcess.ExitCode)." }
        if (Test-Path -LiteralPath (Join-Path $dataRoot 'settings.json')) {
            throw 'Closing the starter without choosing a Pokemon unexpectedly created a player save.'
        }
        return [ordered]@{
            passed = $true; title = $title; clientWidth = $width; clientHeight = $height
            windowLookup = 'EnumWindows: own process, visible, exact starter title'; closeRequest = 'PostMessage(WM_CLOSE)'
            exitCode = $smokeProcess.ExitCode; playerSaveCreated = $false; manualDesktopPlay = $false
        }
    }
    finally {
        if ($null -ne $smokeProcess) {
            try {
                $smokeProcess.Refresh()
                if (-not $smokeProcess.HasExited) {
                    # Only the process created by this invocation is eligible for cleanup.
                    $smokeProcess.Kill()
                    [void]$smokeProcess.WaitForExit(5000)
                }
            }
            finally { $smokeProcess.Dispose() }
        }
    }
}

$report = [ordered]@{
    passed = $false; timestampUtc = [DateTime]::UtcNow.ToString('o'); commit = $env:GITHUB_SHA
    runtimeIdentifier = 'win-x64'; targetFramework = $null; includedRuntime = $null
    files = @(); nativeImages = @(); nativeStartupRequested = [bool]$LaunchSmokeTest
    nativeStartup = $null; error = $null
}
try {
    $nativeFiles = @('DeskPokemon.exe', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll')
    $requiredFiles = $nativeFiles + @(
        'DeskPokemon.dll', 'DeskPokemon.runtimeconfig.json', 'DeskPokemon.deps.json', 'System.Private.CoreLib.dll',
        'Avalonia.Base.dll', 'Avalonia.Controls.dll', 'Avalonia.Desktop.dll', 'Avalonia.Win32.dll',
        'Avalonia.Markup.Xaml.dll', 'Avalonia.Themes.Fluent.dll', 'Avalonia.Skia.dll', 'SkiaSharp.dll', 'HarfBuzzSharp.dll'
    )
    $report.files = @(foreach ($name in $requiredFiles) {
        $path = Join-Path $publishRoot $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing required published file: $name" }
        $file = Get-Item -LiteralPath $path
        if ($file.Length -eq 0) { throw "Empty required published file: $name" }
        [ordered]@{ file = $name; bytes = $file.Length; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    })
    $report.nativeImages = @(foreach ($name in $nativeFiles) { Read-X64Image (Join-Path $publishRoot $name) })
    $runtime = Get-Content -LiteralPath (Join-Path $publishRoot 'DeskPokemon.runtimeconfig.json') -Raw | ConvertFrom-Json -AsHashtable
    $options = $runtime['runtimeOptions']
    if ($null -eq $options -or $options['tfm'] -ne 'net10.0') { throw 'Expected a net10.0 runtime configuration.' }
    if ($options.Contains('framework') -or $options.Contains('frameworks') -or -not $options.Contains('includedFrameworks')) {
        throw 'Expected a self-contained runtime configuration with includedFrameworks and no external framework requirement.'
    }
    $frameworks = @($options['includedFrameworks'] | Where-Object { $_['name'] -eq 'Microsoft.NETCore.App' })
    if ($frameworks.Count -ne 1 -or $frameworks[0]['version'] -notmatch '^10\.') {
        throw 'Expected exactly one included Microsoft.NETCore.App 10.x runtime.'
    }
    $deps = Get-Content -LiteralPath (Join-Path $publishRoot 'DeskPokemon.deps.json') -Raw | ConvertFrom-Json -AsHashtable
    if ($deps['runtimeTarget']['name'] -ne '.NETCoreApp,Version=v10.0/win-x64') {
        throw 'The published dependency manifest does not target net10.0/win-x64.'
    }
    $report.targetFramework = $options['tfm']
    $report.includedRuntime = $frameworks[0]['version']
    if ($LaunchSmokeTest) { $report.nativeStartup = Invoke-StartupSmoke (Join-Path $publishRoot 'DeskPokemon.exe') }
    $report.passed = $true
    Write-Host "PASS Windows x64 self-contained publish: $($requiredFiles.Count) required files, $($nativeFiles.Count) AMD64 native images."
    if ($LaunchSmokeTest) { Write-Host 'PASS Published Windows starter window opened and closed cleanly without creating a player save.' }
}
catch {
    $report.error = $_.Exception.Message
    throw
}
finally {
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportFile -Encoding utf8
    Write-Host "Windows validation report: $reportFile"
}
