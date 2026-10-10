using System.Text.Json;

namespace mStatusBar.Core;

public sealed record MusicState(bool IsPlaying, string Title, string Instrument, bool IsLoop,
    double TotalSeconds, double ElapsedSeconds, double RemainingSeconds);
public sealed record NearbyPerformance(string Title, string Performer, double Distance)
{
    public static NearbyPerformance? ParseNearest(JsonElement source)
    {
        if (source.ValueKind != JsonValueKind.Array) throw new CliException("주변 연주 응답 형식이 올바르지 않습니다.");
        return source.EnumerateArray()
            .Where(player => JsonValue.Property(player, "Performance") is { } performance && JsonValue.Bool(performance, "IsPlaying"))
            .Select(player =>
            {
                var performance = JsonValue.Property(player, "Performance")!.Value;
                var distance = JsonValue.Property(player, "Distance") is { ValueKind: JsonValueKind.Number } number && number.TryGetDouble(out var value) && double.IsFinite(value) && value >= 0 ? value : double.PositiveInfinity;
                var title = JsonValue.Text(performance, "MusicTitle");
                return new NearbyPerformance(string.IsNullOrWhiteSpace(title) ? "제목을 알 수 없는 주변 연주" : title, JsonValue.Text(player, "RealmName"), distance);
            })
            .OrderBy(player => player.Distance).ThenBy(player => player.Title, StringComparer.Ordinal).ThenBy(player => player.Performer, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
public sealed record ActivityState(string Summary, IReadOnlyList<string> Details, MusicState Music)
{
    public bool IsDead { get; init; }
    public static ActivityState Parse(JsonElement source)
    {
        if (source.ValueKind != JsonValueKind.Object) throw new CliException("행동 상태 응답 형식이 올바르지 않습니다.");
        var auto = JsonValue.Property(source, "autoPlay") ?? source;
        var travel = JsonValue.Property(source, "autoTravel") ?? source;
        var combat = JsonValue.Property(source, "combatState") ?? source;
        var dialogue = JsonValue.Property(source, "dialogue") ?? source;
        var mode = JsonValue.Property(source, "Mode") ?? default;
        var details = new List<string>();
        if (JsonValue.Bool(combat, "IsDead")) details.Add(JsonValue.Bool(combat, "IsReviving") ? "부활 중" : "쓰러진 상태");
        else if (JsonValue.Bool(combat, "IsReviving")) details.Add("부활 중");
        if (JsonValue.Bool(dialogue, "IsDialoguePlaying")) details.Add(JsonValue.Bool(dialogue, "IsWaitingForSelection") ? "대화 선택 대기" : "대화 진행 중");
        if (JsonValue.Bool(combat, "IsInCombat")) details.Add("전투 중");
        if (JsonValue.Bool(travel, "IsAutoTraveling")) details.Add("자동 이동 중");
        if (JsonValue.Bool(auto, "IsAutoPlaying"))
        {
            var target = JsonValue.Text(auto, "AutoPlayTargetDisplayName");
            details.Add(target.Length == 0 ? "자동 진행 중" : target + " 진행 중");
        }
        var performance = JsonValue.Property(source, "Performance") ?? default;
        var music = new MusicState(JsonValue.Bool(performance, "IsPlaying"),
            JsonValue.Text(performance, "MusicTitle", "제목 없는 악보"), JsonValue.Text(performance, "InstrumentName"),
            JsonValue.Bool(performance, "IsLoop"), Math.Max(0, JsonValue.Number(performance, "TotalDurationSeconds")),
            Math.Max(0, JsonValue.Number(performance, "ElapsedSeconds")), Math.Max(0, JsonValue.Number(performance, "RemainingSeconds")));
        if (music.IsPlaying) details.Add("연주 중");
        var interaction = JsonValue.Property(source, "Interaction") ?? default;
        if (JsonValue.Bool(interaction, "HasTarget"))
        {
            var running = JsonValue.Text(interaction, "LastRunningInteractionType");
            if (running is "Gather" or "Gathering") details.Add("채집 중");
            else if (running.Length > 0 && running is not ("None" or "PlayMusic")) details.Add("대상과 상호작용 중");
        }
        if (JsonValue.Text(mode, "MainButtonState") is "Fishing" or "FishingPull") details.Add("낚시 중");
        var dungeon = JsonValue.Property(source, "Dungeon") ?? default;
        var dungeonState = JsonValue.Text(dungeon, "State");
        if (dungeonState.Length > 0 && dungeonState != "NotInDungeon") details.Add(JsonValue.Bool(dungeon, "IsBossBattleInProgress") ? "던전 보스전" : "던전 진행 중");
        foreach (var (section, field, label) in new[] {
            ("Battlefield", "IsInBattleField", "전장 참여 중"), ("Scenario", "IsInScenario", "시나리오 진행 중"),
            ("Tutorial", "IsPlaying", "튜토리얼 진행 중"), ("Mode", "IsPlayingMiniGame", "미니게임 진행 중"),
            ("Mode", "IsHousingEditMode", "마이홈 편집 중") })
            if (JsonValue.Property(source, section) is { } value && JsonValue.Bool(value, field)) details.Add(label);
        if (JsonValue.Bool(source, "IsAbyssResultSequencePlaying")) details.Add("심연 결과 처리 중");
        if (JsonValue.Text(mode, "SitState") == "Sitting") details.Add("의자에 앉아 있음");
        if (details.Count == 0 && JsonValue.Text(mode, "MainButtonState") == "Stop") details.Add("행동 진행 중");
        return new ActivityState(details.FirstOrDefault() ?? "대기 중", details.Distinct().ToArray(), music)
            { IsDead = JsonValue.Bool(combat, "IsDead") };
    }
}

public sealed record EnvironmentState(string Weather, string TimeText)
{
    public static EnvironmentState Parse(JsonElement source)
    {
        if (source.ValueKind != JsonValueKind.Object) throw new CliException("환경 상태 응답 형식이 올바르지 않습니다.");
        var weather = JsonValue.Text(source, "Weather");
        var time = JsonValue.Property(source, "ErinnNow") is { } erinn ? ParseTime(erinn) : null;
        if (weather.Length == 0 || time is null) throw new CliException("환경 정보에서 날씨 또는 에린 시간(HH:mm)을 읽지 못했습니다.");
        return new(weather, time);
    }

    private static string? ParseTime(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString() ?? "";
            var match = System.Text.RegularExpressions.Regex.Match(text, "(?<!\\d)(\\d{1,2}):(\\d{2})(?!\\d)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var hour) && hour is >= 0 and <= 23 &&
                int.TryParse(match.Groups[2].Value, out var minute) && minute is >= 0 and <= 59)
                return $"{hour:00}:{minute:00}";
            return null;
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "Time", "DisplayText", "Text", "ErinnNow" })
                if (JsonValue.Property(value, name) is { } nested && ParseTime(nested) is { } parsed) return parsed;
            if (TryInt(value, "Hour", out var h) && TryInt(value, "Minute", out var m) && h is >= 0 and <= 23 && m is >= 0 and <= 59)
                return $"{h:00}:{m:00}";
        }
        return null;
    }

    private static bool TryInt(JsonElement source, string name, out int result)
    {
        result = 0;
        return JsonValue.Property(source, name) is { } value &&
            (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out result) ||
             value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out result));
    }
}

public sealed record HealthState(double Current, double Maximum)
{
    public double Ratio => Maximum > 0 ? Math.Clamp(Current / Maximum, 0, 1) : 0;

    public static HealthState Parse(JsonElement source)
    {
        if (source.ValueKind != JsonValueKind.Object) throw new CliException("생명력 응답 형식이 올바르지 않습니다.");
        var vitals = JsonValue.Property(source, "Vitals") ?? source;
        if (!TryNumber(JsonValue.Property(vitals, "HealthCurrent"), out var current) ||
            !TryNumber(JsonValue.Property(vitals, "HealthMax"), out var maximum) || current < 0 || maximum < 0)
            throw new CliException("현재 생명력 또는 최대 생명력을 읽지 못했습니다.");
        return new(current, maximum);
    }

    private static bool TryNumber(JsonElement? value, out double result)
    {
        result = 0;
        if (value is not { } item) return false;
        if (item.ValueKind == JsonValueKind.Object) value = JsonValue.Property(item, "Value");
        if (value is not { } scalar) return false;
        return scalar.ValueKind switch
        {
            JsonValueKind.Number => scalar.TryGetDouble(out result) && double.IsFinite(result),
            JsonValueKind.String => double.TryParse(scalar.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out result) && double.IsFinite(result),
            _ => false
        };
    }
}

public sealed record WorkState(string Name, string Facility, string State, bool Completed, double RemainingSeconds)
{
    public string StatusText => Completed ? "완료 · 수령 가능" : State == "NotStarted" ? "대기 중" : "진행 중";
}
public sealed record WorkQueue(IReadOnlyList<WorkState> Works, int CompletedCount)
{
    public int WaitingCount => Works.Count(w => !w.Completed && w.State == "NotStarted");
    public int RunningCount => Works.Count(w => !w.Completed && w.State == "InProgress");
    public string Summary => $"진행 {RunningCount} · 완료 {CompletedCount}";

    public static WorkQueue Parse(JsonElement source)
    {
        if (JsonValue.Property(source, "works") is not { ValueKind: JsonValueKind.Array } works)
            throw new CliException("작업 큐 응답 형식이 올바르지 않습니다.");
        var result = works.EnumerateArray().Select(work => new WorkState(
            JsonValue.Text(work, "DisplayName", "이름 없는 품목"), JsonValue.Text(work, "FacilityName", "기타 시설"),
            JsonValue.Text(work, "State"), JsonValue.Bool(work, "IsCompleted") || JsonValue.Text(work, "State") == "Completed",
            Math.Max(0, JsonValue.Number(work, "RemainingSeconds")))).ToArray();
        return new(result, result.Count(w => w.Completed));
    }
}

public sealed record StatusSnapshot(bool Connected, string Message, ActivityState? Activity, WorkQueue? Queue,
    DateTimeOffset UpdatedAt, TimeSpan NextPoll, IReadOnlyList<string> Errors)
{
    public NearbyPerformance? NearbyMusic { get; init; }
    public string? NearbyMusicUnavailableReason { get; init; }
    public EnvironmentState? Environment { get; init; }
    public string? EnvironmentUnavailableReason { get; init; }
    public HealthState? Health { get; init; }
    public string? HealthUnavailableReason { get; init; }
    public static StatusSnapshot Unavailable(string message, TimeSpan next) =>
        new(false, message, null, null, DateTimeOffset.Now, next, []);
}

public static class DisplayTime
{
    public static double Elapsed(MusicState music, DateTimeOffset capturedAt, DateTimeOffset now)
    {
        var elapsed = Math.Max(0, music.ElapsedSeconds + Math.Max(0, (now - capturedAt).TotalSeconds));
        return music.TotalSeconds > 0 ? music.IsLoop ? elapsed % music.TotalSeconds : Math.Min(elapsed, music.TotalSeconds) : elapsed;
    }
    public static string Playback(MusicState music, DateTimeOffset capturedAt, DateTimeOffset now, PlaybackTimeMode mode)
    {
        var elapsed = Elapsed(music, capturedAt, now);
        var seconds = mode == PlaybackTimeMode.Elapsed ? elapsed : music.TotalSeconds > 0 ? music.TotalSeconds - elapsed : double.NaN;
        return (mode == PlaybackTimeMode.Elapsed ? "재생 " : "남은 ") + Duration(seconds);
    }
    public static string Duration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return "--:--";
        var time = TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds - 1));
        return time.TotalHours >= 1 ? $"{(long)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{(long)time.TotalMinutes:00}:{time.Seconds:00}";
    }

    public static string Music(MusicState music, DateTimeOffset capturedAt, DateTimeOffset now)
    {
        if (!music.IsPlaying) return "연주 없음";
        var elapsed = Elapsed(music, capturedAt, now);
        return $"{music.Title} · {Duration(elapsed)} / {(music.TotalSeconds > 0 ? Duration(music.TotalSeconds) : "--:--")}";
    }
}
