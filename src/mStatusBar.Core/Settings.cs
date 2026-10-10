using System.Text.Json;

namespace mStatusBar.Core;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<TaskbarPlacement>))]
public enum TaskbarPlacement { BeforeNotificationArea, LeftEdge, BeforeStartButton, Auto, Center }

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<MusicEffect>))]
public enum MusicEffect { Equalizer, Wave, Pulse, Orbit, None }
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<PlaybackTimeMode>))]
public enum PlaybackTimeMode { Elapsed, Remaining }

public sealed record AppSettings
{
    public string CliPath { get; init; } = "";
    public int PollIntervalSeconds { get; init; } = 2;
    public TaskbarPlacement Placement { get; init; } = TaskbarPlacement.BeforeNotificationArea;
    public MusicEffect PlaybackEffect { get; init; } = MusicEffect.Equalizer;
    public string BackgroundColor { get; init; } = "#171D28";
    public int BackgroundTransparencyPercent { get; init; } = 8;
    public PlaybackTimeMode MusicTimeDisplay { get; init; } = PlaybackTimeMode.Elapsed;
    public bool StartWithWindows { get; init; }

    public void Validate()
    {
        if (!Enum.IsDefined(Placement)) throw new ArgumentException("지원하지 않는 작업 표시줄 위치입니다.");
        if (!Enum.IsDefined(PlaybackEffect)) throw new ArgumentException("지원하지 않는 음악 재생 효과입니다.");
        if (!Enum.IsDefined(MusicTimeDisplay)) throw new ArgumentException("지원하지 않는 재생 시간 표시입니다.");
        if (BackgroundColor is null || BackgroundColor.Length != 7 || BackgroundColor[0] != '#' || !BackgroundColor[1..].All(Uri.IsHexDigit))
            throw new ArgumentException("배경색은 #171D28처럼 6자리 색상 코드로 입력해 주세요.");
        if (BackgroundTransparencyPercent is < 0 or > 100) throw new ArgumentException("배경 투명도는 0~100%여야 합니다.");
        if (PollIntervalSeconds is < 1 or > 60)
            throw new ArgumentException("조회 주기는 1~60초의 정수여야 합니다.");
        if (CliPath.Length > 0 && (!Path.IsPathFullyQualified(CliPath) ||
            !string.Equals(Path.GetExtension(CliPath), ".exe", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("CLI 실행 파일의 전체 경로를 선택해 주세요.");
    }
}

public sealed class SettingsStore(string directory)
{
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mStatusBar");
    public string DirectoryPath { get; } = directory;
    public string SettingsPath => Path.Combine(DirectoryPath, "settings.json");
    public string? LoadWarning { get; private set; }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath))
                ?? throw new JsonException("설정이 비어 있습니다.");
            settings.Validate();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            LoadWarning = $"설정을 읽지 못해 기본값을 사용합니다. {ex.Message}";
            return new();
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Validate();
        AtomicWrite(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void AtomicWrite(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content, new System.Text.UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

