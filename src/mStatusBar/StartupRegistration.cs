using Microsoft.Win32;

namespace mStatusBar;

internal interface IStartupRegistration { void Apply(bool enabled); }
internal sealed class WindowsStartupRegistration : IStartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public void Apply(bool enabled)
    {
        if (enabled)
        {
            var path = Environment.ProcessPath ?? throw new InvalidOperationException("자동 실행할 프로그램 경로를 확인하지 못했습니다.");
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true);
            key.SetValue("mStatusBar", BuildCommand(path), RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true);
            key?.DeleteValue("mStatusBar", false);
        }
    }
    internal static string BuildCommand(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Contains('"')) throw new ArgumentException("자동 실행 경로가 올바르지 않습니다.");
        return "\"" + path + "\"";
    }
}
// Smoke tests never modify the user's Windows startup registration.
internal sealed class MockStartupRegistration : IStartupRegistration
{
    public bool Enabled { get; private set; }
    public void Apply(bool enabled) => Enabled = enabled;
}
