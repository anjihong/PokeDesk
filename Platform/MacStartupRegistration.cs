using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DeskPokemon.Platform;

/// <summary>다음 사용자 로그인에 적용하는 LaunchAgent. 등록 시 앱을 다시 실행하거나 권한을 요청하지 않는다.</summary>
internal sealed class MacStartupRegistration(
    string launchAgentsDirectory, Func<string?> executable, Func<string, bool>? fileExists = null)
    : StartupRegistrationBase(executable, fileExists)
{
    internal const string Label = "io.github.anjihong.PokeDesk";
    internal string RegistrationPath => Path.Combine(launchAgentsDirectory, Label + ".plist");

    protected override bool IsAbsolutePath(string path) => path.StartsWith('/');

    protected override bool IsRegistered()
    {
        if (!File.Exists(RegistrationPath)) return false;
        using var reader = XmlReader.Create(RegistrationPath, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
        });
        var document = XDocument.Load(reader);
        var dictionary = document.Root?.Name == "plist" ? document.Root.Element("dict") : null;
        if (dictionary == null) throw new InvalidDataException("LaunchAgent 형식이 올바르지 않습니다.");
        XElement? Value(string key) => dictionary.Elements("key").FirstOrDefault(element => element.Value == key)
            ?.ElementsAfterSelf().FirstOrDefault();
        if (Value("Label")?.Value != Label || Value("ProgramArguments")?.Elements("string").Any() != true)
            throw new InvalidDataException("PokeDesk LaunchAgent 정보를 확인할 수 없습니다.");
        return Value("RunAtLoad")?.Name == "true" && Value("Disabled")?.Name != "true";
    }

    protected override void Register(string executablePath)
    {
        // ProgramArguments의 각 string이 한 인자다. 공백/따옴표/&는 XML 인코더가 보존하며 셸은 사용하지 않는다.
        var document = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement("plist", new XAttribute("version", "1.0"), new XElement("dict",
                new XElement("key", "Label"), new XElement("string", Label),
                new XElement("key", "ProgramArguments"), new XElement("array", new XElement("string", executablePath)),
                new XElement("key", "RunAtLoad"), new XElement("true"),
                new XElement("key", "LimitLoadToSessionType"), new XElement("string", "Aqua"))));
        Directory.CreateDirectory(launchAgentsDirectory);
        var temporary = RegistrationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var writer = XmlWriter.Create(temporary, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
                document.Save(writer);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, RegistrationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    protected override void Unregister()
    {
        if (File.Exists(RegistrationPath)) File.Delete(RegistrationPath);
    }
}
