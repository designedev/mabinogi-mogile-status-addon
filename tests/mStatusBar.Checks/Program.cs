using System.Text;
using System.Text.Json;
using mStatusBar.Core;

if (args.Length > 0)
{
    var commandIndex = args[0] == "--mock" ? 2 : 0;
    var command = args[commandIndex];
    if (CliProcess.AllowedCommands.Contains(command) || command is "complete_altering_work" or "play_music_score")
    {
        var fixture = args.Length >= 3 && args[0] == "--mock" ? args[1] : "";
        if (fixture.Length > 0)
        {
            File.AppendAllText(Path.Combine(fixture, "calls.txt"), command + "\n");
            if (command is "complete_altering_work" or "play_music_score")
            {
                var raw = args[commandIndex + 1];
                File.WriteAllText(Path.Combine(fixture, "body.json"), Encoding.UTF8.GetString(Convert.FromBase64String(raw[7..])));
                if (File.Exists(Path.Combine(fixture, "slow-action")))
                {
                    File.WriteAllText(Path.Combine(fixture, "action-started"), "");
                    await Task.Delay(1500);
                }
            }
            if (File.Exists(Path.Combine(fixture, "hold"))) await Task.Delay(30_000);
            if (File.Exists(Path.Combine(fixture, "disconnect")))
            {
                Console.Write("{\"pipe\":\"disconnected\",\"reason\":\"option_off\"}"); return 5;
            }
            if (command == "get_activity" && File.Exists(Path.Combine(fixture, "unknown")))
            {
                File.Delete(Path.Combine(fixture, "unknown")); Console.Write("{\"error\":\"unknown_command\"}"); return 4;
            }
            if (command == "get_near_pcs" && File.Exists(Path.Combine(fixture, "near-unknown")))
            {
                File.Delete(Path.Combine(fixture, "near-unknown")); Console.Write("{\"error\":\"unknown_command\"}"); return 4;
            }
            if (command == "get_near_pcs" && File.Exists(Path.Combine(fixture, "near-disconnect"))) { Console.Write("{\"pipe\":\"disconnected\",\"reason\":\"game_off\"}"); return 5; }
            if (command == "get_near_pcs" && File.Exists(Path.Combine(fixture, "near-error"))) { Console.Write("{\"status\":\"rejected\",\"error\":\"not_available\"}"); return 0; }
            if (command == "get_current_environment" && File.Exists(Path.Combine(fixture, "environment-error"))) { Console.Write("{\"status\":\"rejected\",\"error\":\"not_available\"}"); return 0; }
            if (command == "get_my_info" && File.Exists(Path.Combine(fixture, "vitals-error"))) { Console.Write("{\"status\":\"rejected\",\"error\":\"not_available\"}"); return 0; }
        }
        var output = command switch
        {
            "status" => "{\"pipe\":\"connected\"}",
            "capabilities" => "{\"commands\":[{\"Command\":\"get_activity\"},{\"Command\":\"get_altering_works\"},{\"Command\":\"get_music_scores\"},{\"Command\":\"get_near_pcs\"},{\"Command\":\"get_current_environment\"},{\"Command\":\"get_my_info\"},{\"Command\":\"complete_altering_work\"},{\"Command\":\"play_music_score\",\"Metadata\":{\"requiresConfirm\":true},\"Note\":\"Running this command consumes 5 test wings.\"}]}",
            "get_near_pcs" => "[{\"RealmName\":\"멀리 있는 연주자\",\"Distance\":20,\"Performance\":{\"IsPlaying\":true,\"MusicTitle\":\"먼 노래\"}},{\"RealmName\":\"가까운 연주자\",\"Distance\":3,\"Performance\":{\"IsPlaying\":true,\"MusicTitle\":\"바람이 머무는 자리\"}}]",
            "get_music_scores" => "[{\"DisplayTitle\":\"다시 만나는 날\"},{\"DisplayTitle\":\"나의 노래\"},{\"DisplayTitle\":\"가을의 기억\"},{\"DisplayTitle\":\"달빛 소나타 🎵\"}]",
            "complete_altering_work" => "{\"status\":\"accepted\",\"body\":{\"collected\":1,\"message\":\"완료품을 수령했습니다.\"}}",
            "play_music_score" => "{\"status\":\"accepted\",\"body\":{\"result\":\"started\"}}",
            "get_activity" => "{\"status\":\"accepted\",\"body\":{\"autoTravel\":{\"IsAutoTraveling\":true},\"Performance\":{\"IsPlaying\":true,\"MusicTitle\":\"달빛 소나타 🎵\",\"InstrumentName\":\"피아노\",\"TotalDurationSeconds\":240,\"ElapsedSeconds\":80,\"RemainingSeconds\":160}}}",
            "get_current_environment" => "{\"status\":\"accepted\",\"body\":{\"Weather\":\"Sunny\",\"ErinnNow\":\"2962-2-01 10:44\"}}",
            "get_my_info" => "{\"status\":\"accepted\",\"body\":{\"Vitals\":{\"HealthCurrent\":92928,\"HealthMax\":92928}}}",
            _ => "{\"status\":\"accepted\",\"body\":{\"completedCount\":1,\"works\":[{\"DisplayName\":\"철괴\",\"FacilityName\":\"대장간\",\"State\":\"Completed\",\"IsCompleted\":true},{\"DisplayName\":\"목재\",\"FacilityName\":\"제재소\",\"State\":\"InProgress\",\"RemainingSeconds\":120},{\"DisplayName\":\"나무판\",\"FacilityName\":\"제재소\",\"State\":\"NotStarted\",\"RemainingSeconds\":180}]}}"
        };
        if (command == "get_activity" && File.Exists(Path.Combine(fixture, "no-own-music"))) output = output.Replace("\"IsPlaying\":true", "\"IsPlaying\":false");
        if (command == "get_near_pcs" && File.Exists(Path.Combine(fixture, "no-near-music"))) output = "[]";
        if (command == "capabilities" && File.Exists(Path.Combine(fixture, "near-unavailable"))) output = output.Replace("{\"Command\":\"get_near_pcs\"},", "");
        if (command == "capabilities" && File.Exists(Path.Combine(fixture, "near-confirm"))) output = output.Replace("{\"Command\":\"get_near_pcs\"}", "{\"Command\":\"get_near_pcs\",\"Metadata\":{\"requiresConfirm\":true}}");
        if (command == "capabilities" && File.Exists(Path.Combine(fixture, "environment-unavailable"))) output = output.Replace("{\"Command\":\"get_current_environment\"},", "");
        if (command == "capabilities" && File.Exists(Path.Combine(fixture, "vitals-unavailable"))) output = output.Replace("{\"Command\":\"get_my_info\"},", "");
        if (command == "capabilities" && File.Exists(Path.Combine(fixture, "environment-confirm"))) output = output.Replace("{\"Command\":\"get_current_environment\"}", "{\"Command\":\"get_current_environment\",\"Metadata\":{\"requiresConfirm\":true}}");
        if (command == "capabilities" && File.Exists(Path.Combine(fixture, "vitals-confirm"))) output = output.Replace("{\"Command\":\"get_my_info\"}", "{\"Command\":\"get_my_info\",\"Metadata\":{\"requiresConfirm\":true}}");
        if (Environment.GetEnvironmentVariable("MSTATUSBAR_MOCK_LONG_TITLE") is { Length: > 0 } longTitle)
            output = output.Replace("달빛 소나타 🎵", longTitle);
        if (command == "get_music_scores" && Environment.GetEnvironmentVariable("MSTATUSBAR_MOCK_MORE_SCORES") == "1")
        {
            var existing = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(output)!;
            existing.AddRange(Enumerable.Range(1, 20).Select(i => new Dictionary<string, string> { ["DisplayTitle"] = $"연습곡 {i:00}" }));
            output = JsonSerializer.Serialize(existing);
        }
        var bytes = Encoding.UTF8.GetBytes(output);
        using var stdout = Console.OpenStandardOutput();
        // Split multibyte characters across writes to exercise the actual decoder.
        for (var i = 0; i < bytes.Length; i += 7)
            await stdout.WriteAsync(bytes.AsMemory(i, Math.Min(7, bytes.Length - i)));
        return 0;
    }
}

var root = Path.Combine(Path.GetTempPath(), "mStatusBar-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passed = 0;
async Task Check(string name, Func<Task> run)
{
    await run(); passed++; Console.WriteLine("PASS " + name);
}
void Assert(bool condition, string message = "assertion failed") { if (!condition) throw new Exception(message); }
JsonElement Json(string source) => JsonDocument.Parse(source).RootElement.Clone();
async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name);
}
string NewFixture()
{
    var path = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path;
}

await Check("settings default is two seconds and round trips", () =>
{
    var store = new SettingsStore(NewFixture());
    Assert(store.Load().PollIntervalSeconds == 2);
    var settings = new AppSettings { CliPath = Path.Combine(root, "한글 폴더", "CLI.exe"), PollIntervalSeconds = 7 };
    store.Save(settings); Assert(store.Load() == settings); return Task.CompletedTask;
});
await Check("corrupt settings preserve file and warn", () =>
{
    var store = new SettingsStore(NewFixture()); File.WriteAllText(store.SettingsPath, "broken");
    Assert(store.Load().PollIntervalSeconds == 2 && store.LoadWarning is not null);
    Assert(File.ReadAllText(store.SettingsPath) == "broken"); return Task.CompletedTask;
});
await Check("placement defaults migrate and all options persist", () =>
{
    var store = new SettingsStore(NewFixture());
    File.WriteAllText(store.SettingsPath, "{\"PollIntervalSeconds\":2}");
    Assert(store.Load().Placement == TaskbarPlacement.BeforeNotificationArea);
    foreach (var position in Enum.GetValues<TaskbarPlacement>())
    {
        store.Save(new() { Placement = position });
        Assert(store.Load().Placement == position);
    }
    return Task.CompletedTask;
});
await Check("center placement fits nearest safe position across layout and DPI sizes", () =>
{
    Assert(TaskbarCenterPlacement.Left(1920, 600, 1700, 480) == 720);
    Assert(TaskbarCenterPlacement.Left(1920, 1000, 1700, 480) == 1000);
    Assert(TaskbarCenterPlacement.Left(1920, 0, 650, 480) == 170);
    Assert(TaskbarCenterPlacement.Left(2560, 900, 2300, 720) == 920);
    Assert(TaskbarCenterPlacement.Left(1920, 1300, 1500, 200) == 1300);
    return Task.CompletedTask;
});
await Check("unsupported placement is rejected", async () =>
{
    await Throws<ArgumentException>(() => { new AppSettings { Placement = (TaskbarPlacement)999 }.Validate(); return Task.CompletedTask; });
});
await Check("appearance defaults migrate and effects colors transparency persist", () =>
{
    var store = new SettingsStore(NewFixture());
    File.WriteAllText(store.SettingsPath, "{\"PollIntervalSeconds\":2}");
    var defaults = store.Load();
    Assert(defaults.PlaybackEffect == MusicEffect.Equalizer && defaults.BackgroundColor == "#171D28" && defaults.BackgroundTransparencyPercent == 8);
    foreach (var effect in Enum.GetValues<MusicEffect>())
        foreach (var transparency in new[] { 0, 45, 100 })
        {
            var settings = defaults with { PlaybackEffect = effect, BackgroundColor = "#123AbC", BackgroundTransparencyPercent = transparency };
            store.Save(settings); Assert(store.Load() == settings);
        }
    return Task.CompletedTask;
});
await Check("playback time and startup settings migrate persist and reject invalid mode", async () =>
{
    var store = new SettingsStore(NewFixture()); File.WriteAllText(store.SettingsPath, "{\"PollIntervalSeconds\":2}");
    Assert(store.Load().MusicTimeDisplay == PlaybackTimeMode.Elapsed && !store.Load().StartWithWindows);
    foreach (var mode in Enum.GetValues<PlaybackTimeMode>())
    {
        var settings = new AppSettings { MusicTimeDisplay = mode, StartWithWindows = true };
        store.Save(settings); Assert(store.Load() == settings);
    }
    await Throws<ArgumentException>(() => { new AppSettings { MusicTimeDisplay = (PlaybackTimeMode)999 }.Validate(); return Task.CompletedTask; });
});
await Check("playback time clamps end wraps loops and handles unknown duration", () =>
{
    var at = DateTimeOffset.Now; var music = new MusicState(true, "곡", "악기", false, 100, 90, 10);
    Assert(DisplayTime.Playback(music, at, at.AddSeconds(5), PlaybackTimeMode.Elapsed) == "재생 01:35");
    Assert(DisplayTime.Playback(music, at, at.AddSeconds(5), PlaybackTimeMode.Remaining) == "남은 00:05");
    Assert(DisplayTime.Elapsed(music, at, at.AddSeconds(30)) == 100);
    Assert(DisplayTime.Elapsed(music with { IsLoop = true }, at, at.AddSeconds(30)) == 20);
    Assert(DisplayTime.Playback(music with { TotalSeconds = 0 }, at, at, PlaybackTimeMode.Remaining) == "남은 --:--");
    Assert(DisplayTime.Elapsed(music, at, at.AddSeconds(-5)) == 90); return Task.CompletedTask;
});
await Check("invalid appearance is rejected without overwriting saved settings", async () =>
{
    var store = new SettingsStore(NewFixture()); var original = new AppSettings(); store.Save(original);
    foreach (var color in new[] { "", "123456", "#FFF", "#XYZ123", "#12345678" })
        await Throws<ArgumentException>(() => { store.Save(original with { BackgroundColor = color }); return Task.CompletedTask; });
    foreach (var value in new[] { -1, 101 })
        await Throws<ArgumentException>(() => { store.Save(original with { BackgroundTransparencyPercent = value }); return Task.CompletedTask; });
    await Throws<ArgumentException>(() => { store.Save(original with { PlaybackEffect = (MusicEffect)999 }); return Task.CompletedTask; });
    Assert(store.Load() == original);
});
await Check("invalid intervals and relative paths are rejected", async () =>
{
    foreach (var value in new[] { 0, -1, 61 }) await Throws<ArgumentException>(() => { new AppSettings { PollIntervalSeconds = value }.Validate(); return Task.CompletedTask; });
    await Throws<ArgumentException>(() => { new AppSettings { CliPath = "CLI.exe" }.Validate(); return Task.CompletedTask; });
});
await Check("response wrapper BOM and escaped Korean", () =>
{
    var data = CliProcess.Parse("\uFEFF{\"status\":\"accepted\",\"body\":{\"name\":\"\\uD55C\\uAE00\"}}", 0);
    Assert(JsonValue.Text(data, "name") == "한글"); return Task.CompletedTask;
});
await Check("accepted error rejected response invalid JSON and disconnect fail", async () =>
{
    foreach (var source in new[] { "{\"status\":\"accepted\",\"body\":{\"error\":\"blocked\"}}", "{\"status\":\"rejected\"}", "{\"pipe\":\"disconnected\",\"reason\":\"game_off\"}", "invalid" })
        await Throws<CliException>(() => { CliProcess.Parse(source, 0); return Task.CompletedTask; });
    await Throws<CliException>(() => { CliProcess.Parse("{}", 5); return Task.CompletedTask; });
});
await Check("capabilities wrappers string approval loading and empty schema", async () =>
{
    Assert(ReadOnlyCliClient.ParseCapabilities(Json("{\"body\":{\"Commands\":[{\"Command\":\"get_activity\",\"Metadata\":{\"requiresConfirm\":\"true\"}}]}}"))["get_activity"]);
    foreach (var source in new[] { "{\"loading\":true}", "{\"commands\":[]}", "{\"commands\":[{\"Command\":\"get_activity\",\"Metadata\":{\"requiresConfirm\":123}}]}" })
        await Throws<CliException>(() => { ReadOnlyCliClient.ParseCapabilities(Json(source)); return Task.CompletedTask; });
});
await Check("flat and nested activity preserve game labels", () =>
{
    var flat = ActivityState.Parse(Json("{\"IsAutoPlaying\":true,\"AutoPlayTargetDisplayName\":\"여신의 임무\"}"));
    var nested = ActivityState.Parse(Json("{\"autoPlay\":{\"IsAutoPlaying\":true,\"AutoPlayTargetDisplayName\":\"여신의 임무\"}}"));
    Assert(flat.Summary == nested.Summary && flat.Summary.Contains("여신의 임무"));
    Assert(ActivityState.Parse(Json("{\"IsAutoPlaying\":true,\"AutoPlayTarget\":\"quest\"}")).Summary == "자동 진행 중");
    return Task.CompletedTask;
});
await Check("activity exposes explicit death state without inferring it from health", () =>
{
    var dead = ActivityState.Parse(Json("{\"combatState\":{\"IsDead\":true}}"));
    var alive = ActivityState.Parse(Json("{\"combatState\":{\"IsDead\":false}}"));
    Assert(dead.IsDead && !alive.IsDead && dead.Summary == "쓰러진 상태"); return Task.CompletedTask;
});
await Check("environment and vitals parse scalar and stat object values", async () =>
{
    var environment = EnvironmentState.Parse(Json("{\"Weather\":\"Sunny\",\"ErinnNow\":\"2962-2-01 10:44\"}"));
    Assert(environment.Weather == "Sunny" && environment.TimeText == "10:44");
    Assert(EnvironmentState.Parse(Json("{\"Weather\":\"Rain\",\"ErinnNow\":{\"Hour\":7,\"Minute\":5}}" )).TimeText == "07:05");
    var scalar = HealthState.Parse(Json("{\"Vitals\":{\"HealthCurrent\":92928,\"HealthMax\":100000}}"));
    var stats = HealthState.Parse(Json("{\"Vitals\":{\"HealthCurrent\":{\"Value\":50},\"HealthMax\":{\"DisplayName\":\"HP\",\"Value\":40}}}"));
    Assert(scalar.Current == 92928 && scalar.Ratio == 0.92928 && stats.Ratio == 1);
    await Throws<CliException>(() => { HealthState.Parse(Json("{\"Vitals\":{\"HealthCurrent\":-1,\"HealthMax\":10}}")); return Task.CompletedTask; });
    Assert(HealthState.Parse(Json("{\"Vitals\":{\"HealthCurrent\":0,\"HealthMax\":0}}")) is { Ratio: 0 });
});
await Check("work queue counts waiting running complete and does not mark waiting running", () =>
{
    var queue = WorkQueue.Parse(Json("{\"works\":[{\"State\":\"NotStarted\"},{\"State\":\"InProgress\",\"RemainingSeconds\":2},{\"State\":\"Completed\"}]}"));
    Assert(queue.WaitingCount == 1 && queue.RunningCount == 1 && queue.CompletedCount == 1);
    Assert(queue.Works[0].StatusText == "대기 중"); return Task.CompletedTask;
});
await Check("music interpolation clamp and loop", () =>
{
    var time = DateTimeOffset.Now;
    var music = new MusicState(true, "테스트", "피아노", false, 100, 95, 5);
    Assert(DisplayTime.Music(music, time, time.AddSeconds(10)).Contains("01:40 / 01:40"));
    Assert(DisplayTime.Music(music with { IsLoop = true }, time, time.AddSeconds(10)).Contains("00:05"));
    return Task.CompletedTask;
});
await Check("backoff caps at 60 and resets to configured interval", () =>
{
    Assert(PollingService.Delay(2, 1).TotalSeconds == 4 && PollingService.Delay(2, 2).TotalSeconds == 8);
    Assert(PollingService.Delay(2, 30).TotalSeconds == 60 && PollingService.Delay(7, 0).TotalSeconds == 7);
    return Task.CompletedTask;
});

var executable = Path.ChangeExtension(typeof(AppSettings).Assembly.Location.Replace("mStatusBar.Core.dll", "mStatusBar.Checks.dll"), ".exe");
await Check("real mock subprocess decodes split UTF8 and denies action commands", async () =>
{
    var fixture = NewFixture(); var runner = new CliProcess(["--mock", fixture]);
    var activity = await runner.RunAsync(executable, "get_activity", default);
    Assert(ActivityState.Parse(activity).Music.Title == "달빛 소나타 🎵");
    await Throws<CliException>(() => runner.RunAsync(executable, "stop_action", default));
    Assert(File.ReadAllLines(Path.Combine(fixture, "calls.txt")).Length == 1);
});
await Check("mock schema cache refresh exit4 retries once", async () =>
{
    var fixture = NewFixture(); File.WriteAllText(Path.Combine(fixture, "unknown"), "");
    var client = new ReadOnlyCliClient(new CliProcess(["--mock", fixture]), Path.Combine(fixture, "cache"), "");
    await client.CheckStatusAsync(executable, default);
    await client.QueryAsync(executable, "get_activity", default);
    await client.QueryAsync(executable, "get_altering_works", default);
    var calls = File.ReadAllLines(Path.Combine(fixture, "calls.txt"));
    Assert(calls.Count(c => c == "capabilities") == 2 && calls.Count(c => c == "get_activity") == 2);
});
await Check("process cancellation stops a held read query", async () =>
{
    var fixture = NewFixture(); File.WriteAllText(Path.Combine(fixture, "hold"), "");
    var runner = new CliProcess(["--mock", fixture]); using var cancellation = new CancellationTokenSource(400);
    await Throws<OperationCanceledException>(() => runner.RunAsync(executable, "status", cancellation.Token));
    File.Delete(Path.Combine(fixture, "hold"));
    await runner.RunAsync(executable, "status", default);
});
await Check("polling waits after completion settings restart never overlap and clears stale data", async () =>
{
    var fixture = NewFixture(); var fake = new FakeTransport();
    await using var poller = new PollingService(new ReadOnlyCliClient(fake, fixture, ""));
    var updates = new List<StatusSnapshot>();
    var arrived = new SemaphoreSlim(0);
    poller.Updated += snapshot => { lock (updates) updates.Add(snapshot); arrived.Release(); };
    await poller.ConfigureAsync(new() { CliPath = executable, PollIntervalSeconds = 1 });
    await arrived.WaitAsync(TimeSpan.FromSeconds(5));
    await arrived.WaitAsync(TimeSpan.FromSeconds(5));
    lock (updates)
    {
        Assert(updates.Count >= 2 && updates[0].Connected && updates[0].Queue?.Works.Count == 0);
        Assert((updates[1].UpdatedAt - updates[0].UpdatedAt).TotalSeconds >= 1);
    }
    fake.Disconnected = true;
    await poller.ConfigureAsync(new() { CliPath = executable, PollIntervalSeconds = 2 });
    Assert(await arrived.WaitAsync(TimeSpan.FromSeconds(5)));
    lock (updates) Assert(!updates[^1].Connected && updates[^1].Activity is null && updates[^1].Queue is null && updates[^1].NextPoll.TotalSeconds == 4);
    fake.Disconnected = false;
    await poller.ConfigureAsync(new() { CliPath = executable, PollIntervalSeconds = 3 });
    Assert(await arrived.WaitAsync(TimeSpan.FromSeconds(5)));
    lock (updates) Assert(updates[^1].Connected && updates[^1].NextPoll.TotalSeconds == 3);
    Assert(fake.MaximumConcurrent == 1);
});
await Check("polling reads environment and vitals sequentially and drops optional failures", async () =>
{
    var fixture = NewFixture();
    await using var poller = new PollingService(new ReadOnlyCliClient(new CliProcess(["--mock", fixture]), Path.Combine(fixture, "cache"), ""));
    var updates = new Queue<StatusSnapshot>(); var changed = new SemaphoreSlim(0);
    poller.Updated += snapshot => { lock (updates) updates.Enqueue(snapshot); changed.Release(); };
    await poller.ConfigureAsync(new() { CliPath = executable, PollIntervalSeconds = 1 });
    Assert(await changed.WaitAsync(TimeSpan.FromSeconds(5)));
    StatusSnapshot healthy; lock (updates) healthy = updates.Dequeue();
    Assert(healthy.Environment is { Weather: "Sunny", TimeText: "10:44" } && healthy.Health is { Current: 92928, Maximum: 92928, Ratio: 1 });
    var firstCalls = File.ReadAllLines(Path.Combine(fixture, "calls.txt"));
    Assert(Array.IndexOf(firstCalls, "get_activity") < Array.IndexOf(firstCalls, "get_altering_works") &&
        Array.IndexOf(firstCalls, "get_altering_works") < Array.IndexOf(firstCalls, "get_current_environment") &&
        Array.IndexOf(firstCalls, "get_current_environment") < Array.IndexOf(firstCalls, "get_my_info"));
    File.WriteAllText(Path.Combine(fixture, "environment-error"), ""); File.WriteAllText(Path.Combine(fixture, "vitals-error"), "");
    await poller.ConfigureAsync(new() { CliPath = executable, PollIntervalSeconds = 2 });
    Assert(await changed.WaitAsync(TimeSpan.FromSeconds(5)));
    StatusSnapshot failed; lock (updates) failed = updates.Dequeue();
    Assert(failed.Connected && failed.Activity is not null && failed.Queue is not null && failed.Environment is null && failed.Health is null);
    Assert(failed.EnvironmentUnavailableReason is not null && failed.HealthUnavailableReason is not null && failed.NextPoll == TimeSpan.FromSeconds(2));
});
await Check("confirmation-required queries are blocked before executing", async () =>
{
    var fake = new FakeTransport { RequireConfirmation = true };
    var client = new ReadOnlyCliClient(fake, NewFixture(), "");
    await Throws<CliException>(() => client.QueryAsync(executable, "get_activity", default));
    Assert(fake.QueryCount == 0);
});
await Check("disconnect during state query clears connected flag and old values", async () =>
{
    var fake = new FakeTransport { DisconnectQueries = true };
    await using var poller = new PollingService(new ReadOnlyCliClient(fake, NewFixture(), ""));
    var first = new TaskCompletionSource<StatusSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
    poller.Updated += snapshot => first.TrySetResult(snapshot);
    await poller.ConfigureAsync(new() { CliPath = executable });
    var result = await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(!result.Connected && result.Activity is null && result.Queue is null && result.NextPoll.TotalSeconds == 4);
});
await Check("owned scores sort in Korean order and collapse identical selectable titles", () =>
{
    var titles = MusicCatalog.Titles(Json("[{\"DisplayTitle\":\"다\"},{\"DisplayTitle\":\"가\"},{\"DisplayTitle\":\"나\"},{\"DisplayTitle\":\"가\"}]"));
    Assert(string.Join(",", titles) == "가,나,다"); return Task.CompletedTask;
});
await Check("collection uses exact Korean item body without extra confirmation for current schema", async () =>
{
    var fixture = NewFixture(); var process = new CliProcess(["--mock", fixture]);
    var service = new CollectionService(process, process);
    var message = await service.CollectAsync(executable, new("철괴", "대장간", "Completed", true, 0), _ => throw new Exception("unexpected confirmation"));
    Assert(message.Contains("수령"));
    Assert(JsonValue.Text(Json(File.ReadAllText(Path.Combine(fixture, "body.json"))), "displayName") == "철괴");
});
await Check("playback executes immediately with explicit user-authorized approval and currency costs", async () =>
{
    var fixture = NewFixture(); var process = new CliProcess(["--mock", fixture]);
    await new MusicService(process, process).PlayAsync(executable, "달빛 소나타 🎵");
    Assert(JsonValue.Text(Json(File.ReadAllText(Path.Combine(fixture, "body.json"))), "title") == "달빛 소나타 🎵");
    Assert(File.ReadAllLines(Path.Combine(fixture, "calls.txt")).Count(c => c == "play_music_score") == 1);
});
await Check("read queries continue while an action waits for final response", async () =>
{
    var fixture = NewFixture(); File.WriteAllText(Path.Combine(fixture, "slow-action"), "");
    var process = new CliProcess(["--mock", fixture]); var action = process.CollectAsync(executable, "철괴");
    using var limit = new CancellationTokenSource(5000);
    while (!File.Exists(Path.Combine(fixture, "action-started"))) await Task.Delay(10, limit.Token);
    await process.RunAsync(executable, "get_activity", limit.Token);
    Assert(!action.IsCompleted); await action;
});
await Check("stale completed work and in-progress work cannot trigger collection", async () =>
{
    var fake = new ActionFake { Missing = true }; var service = new CollectionService(fake, fake);
    await Throws<CliException>(() => service.CollectAsync("mock", new("철괴", "대장간", "Completed", true, 0), _ => Task.FromResult(true)));
    await Throws<CliException>(() => service.CollectAsync("mock", new("철괴", "대장간", "InProgress", false, 1), _ => Task.FromResult(true)));
    Assert(fake.ActionCount == 0);
});
await Check("required collection approval cancel does not start action", async () =>
{
    var fake = new ActionFake { RequireApproval = true };
    var result = await new CollectionService(fake, fake).CollectAsync("mock", new("철괴", "대장간", "Completed", true, 0), _ => Task.FromResult(false));
    Assert(result.Contains("취소") && fake.ActionCount == 0);
});
await Check("missing owned score cannot start playback", async () =>
{
    var fake = new ActionFake { Missing = true };
    await Throws<CliException>(() => new MusicService(fake, fake).PlayAsync("mock", "가")); Assert(fake.ActionCount == 0);
});
await Check("new score query refreshes older schema caches", async () =>
{
    var fixture = NewFixture(); var cache = Path.Combine(fixture, "cache"); Directory.CreateDirectory(cache);
    var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(executable.ToUpperInvariant())));
    File.WriteAllText(Path.Combine(cache, key + ".json"), "{\"commands\":[{\"Command\":\"get_activity\"},{\"Command\":\"get_altering_works\"}]}");
    var client = new ReadOnlyCliClient(new CliProcess(["--mock", fixture]), cache, "");
    Assert(MusicCatalog.Titles(await client.QueryAsync(executable, "get_music_scores", default)).Count == 4);
    Assert(File.ReadAllLines(Path.Combine(fixture, "calls.txt")).Count(c => c == "capabilities") == 1);
});
await Check("environment and vitals optional queries discover once and block approval", async () =>
{
    foreach (var (command, marker) in new[] { ("get_current_environment", "environment-unavailable"), ("get_my_info", "vitals-unavailable") })
    {
        var fixture = NewFixture(); File.WriteAllText(Path.Combine(fixture, marker), "");
        var client = new ReadOnlyCliClient(new CliProcess(["--mock", fixture]), Path.Combine(fixture, "cache"), "");
        var first = command == "get_current_environment" ? await client.QueryEnvironmentAsync(executable, default) : await client.QueryVitalsAsync(executable, default);
        var second = command == "get_current_environment" ? await client.QueryEnvironmentAsync(executable, default) : await client.QueryVitalsAsync(executable, default);
        Assert(first is null && second is null);
        Assert(File.ReadAllLines(Path.Combine(fixture, "calls.txt")).Count(c => c == "capabilities") == 2);
    }
    foreach (var (command, marker) in new[] { ("get_current_environment", "environment-confirm"), ("get_my_info", "vitals-confirm") })
    {
        var fixture = NewFixture(); File.WriteAllText(Path.Combine(fixture, marker), "");
        var client = new ReadOnlyCliClient(new CliProcess(["--mock", fixture]), Path.Combine(fixture, "cache"), "");
        await Throws<CliException>(() => command == "get_current_environment" ? client.QueryEnvironmentAsync(executable, default) : client.QueryVitalsAsync(executable, default));
        Assert(!File.ReadAllLines(Path.Combine(fixture, "calls.txt")).Contains(command));
    }
});
await Check("mock CLI default fixture includes healthy environment and vitals", async () =>
{
    var fixture = NewFixture(); var process = new CliProcess(["--mock", fixture]);
    Assert(EnvironmentState.Parse(await process.RunAsync(executable, "get_current_environment", default)) is { Weather: "Sunny", TimeText: "10:44" });
    Assert(HealthState.Parse(await process.RunAsync(executable, "get_my_info", default)) is { Current: 92928, Maximum: 92928, Ratio: 1 });
});
await Check("flyout placement stays above bottom taskbar and aligns to anchor right edge", () =>
{
    var placed = FlyoutPlacement.Calculate(new(0, 0, 1920, 1032), new(1400, 1032, 480, 48), 480, 580, 6);
    Assert(placed.Left == 1400 && placed.Bottom == 1026 && placed.Right == 1880); return Task.CompletedTask;
});
await Check("nearby music chooses closest playing performer with unknown distance last", async () =>
{
    var nearest = NearbyPerformance.ParseNearest(Json("[{\"RealmName\":\"모름\",\"Performance\":{\"IsPlaying\":true,\"MusicTitle\":\"미상\"}},{\"Distance\":1,\"Performance\":{\"IsPlaying\":false,\"MusicTitle\":\"정지\"}},{\"Distance\":8,\"Performance\":{\"IsPlaying\":true,\"MusicTitle\":\"멀리\"}},{\"RealmName\":\"가까움\",\"Distance\":2,\"Performance\":{\"IsPlaying\":true,\"MusicTitle\":\"근처 노래\"}}]"));
    Assert(nearest is { Title: "근처 노래", Performer: "가까움", Distance: 2 });
    Assert(NearbyPerformance.ParseNearest(Json("[]")) is null);
    Assert(NearbyPerformance.ParseNearest(Json("[{\"Performance\":{\"IsPlaying\":true,\"MusicTitle\":\"\"}}]"))!.Title.Contains("알 수 없는"));
    await Throws<CliException>(() => { NearbyPerformance.ParseNearest(Json("{}")); return Task.CompletedTask; });
});
await Check("optional nearby query refreshes exit4 and persists named capabilities", async () =>
{
    var fixture = NewFixture(); File.WriteAllText(Path.Combine(fixture, "near-unknown"), "");
    var cache = Path.Combine(fixture, "cache"); var client = new ReadOnlyCliClient(new CliProcess(["--mock", fixture]), cache, "");
    Assert(NearbyPerformance.ParseNearest((await client.QueryNearbyAsync(executable, default))!.Value)!.Title == "바람이 머무는 자리");
    var calls = File.ReadAllLines(Path.Combine(fixture, "calls.txt"));
    Assert(calls.Count(c => c == "get_near_pcs") == 2 && calls.Count(c => c == "capabilities") == 2);
    Assert(Directory.GetFiles(cache, "CAPABILITIES.json", SearchOption.AllDirectories).Length == 1);
});
await Check("unsupported nearby query avoids repeated discovery and required approval blocks it", async () =>
{
    var fixture = NewFixture(); File.WriteAllText(Path.Combine(fixture, "near-unavailable"), "");
    var client = new ReadOnlyCliClient(new CliProcess(["--mock", fixture]), Path.Combine(fixture, "cache"), "");
    Assert(await client.QueryNearbyAsync(executable, default) is null); Assert(await client.QueryNearbyAsync(executable, default) is null);
    var calls = File.ReadAllLines(Path.Combine(fixture, "calls.txt"));
    Assert(calls.Count(c => c == "capabilities") == 2 && !calls.Contains("get_near_pcs"));
    var approval = NewFixture(); File.WriteAllText(Path.Combine(approval, "near-confirm"), "");
    await Throws<CliException>(() => new ReadOnlyCliClient(new CliProcess(["--mock", approval]), Path.Combine(approval, "cache"), "").QueryNearbyAsync(executable, default));
    Assert(!File.ReadAllLines(Path.Combine(approval, "calls.txt")).Contains("get_near_pcs"));
});
await Check("polling prioritizes own music and clears stale nearby on silence error disconnect", async () =>
{
    foreach (var marker in new[] { "own", "near", "no-near-music", "near-error", "near-disconnect", "near-unavailable" })
    {
        var fixture = NewFixture();
        if (marker != "own") File.WriteAllText(Path.Combine(fixture, "no-own-music"), "");
        if (marker is not ("own" or "near")) File.WriteAllText(Path.Combine(fixture, marker), "");
        await using var poller = new PollingService(new(new CliProcess(["--mock", fixture]), Path.Combine(fixture, "cache"), ""));
        var first = new TaskCompletionSource<StatusSnapshot>(); poller.Updated += state => first.TrySetResult(state);
        await poller.ConfigureAsync(new() { CliPath = executable });
        var snapshot = await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (marker == "near") Assert(snapshot.NearbyMusic?.Title == "바람이 머무는 자리");
        else Assert(snapshot.NearbyMusic is null);
        Assert(snapshot.Connected == (marker != "near-disconnect"));
        if (marker == "own") Assert(!File.ReadAllLines(Path.Combine(fixture, "calls.txt")).Contains("get_near_pcs"));
        if (marker is "near-error" or "near-unavailable") Assert(snapshot.Activity is not null && snapshot.Queue is not null && snapshot.NearbyMusicUnavailableReason is not null && snapshot.NextPoll == TimeSpan.FromSeconds(2));
        if (marker == "near")
        {
            File.WriteAllText(Path.Combine(fixture, "no-near-music"), "");
            var next = new TaskCompletionSource<StatusSnapshot>(); poller.Updated += state => next.TrySetResult(state);
            Assert((await next.Task.WaitAsync(TimeSpan.FromSeconds(5))).NearbyMusic is null);
        }
    }
});
await Check("flyout placement clamps oversize negative-coordinate and top-taskbar monitors", () =>
{
    var negative = FlyoutPlacement.Calculate(new(-1920, -200, 1920, 1000), new(-600, 800, 480, 48), 2400, 1200, 12);
    Assert(negative.Left == -1920 && negative.Top == -200 && negative.Width == 1920 && negative.Height == 1000);
    var top = FlyoutPlacement.Calculate(new(0, 48, 1920, 1032), new(1400, 0, 480, 48), 720, 600, 6);
    Assert(top.Top == 54 && top.Right == 1880); return Task.CompletedTask;
});
Console.WriteLine($"{passed} checks passed. Isolated fixtures: {root}");
return 0;

sealed class FakeTransport : ICliTransport
{
    private int concurrent;
    public int MaximumConcurrent { get; private set; }
    public bool Disconnected { get; set; }
    public bool DisconnectQueries { get; set; }
    public bool RequireConfirmation { get; set; }
    public int QueryCount { get; private set; }
    public async Task<JsonElement> RunAsync(string path, string command, CancellationToken cancellationToken)
    {
        var count = Interlocked.Increment(ref concurrent); MaximumConcurrent = Math.Max(MaximumConcurrent, count);
        try
        {
            await Task.Delay(80, cancellationToken);
            if (Disconnected) throw new CliException("game_off", 5, "game_off");
            if (command is "get_activity" or "get_altering_works")
            {
                QueryCount++;
                if (DisconnectQueries) throw new CliException("game_off", 5, "game_off");
            }
            if (command == "capabilities" && RequireConfirmation)
                return JsonDocument.Parse("{\"commands\":[{\"Command\":\"get_activity\",\"Metadata\":{\"requiresConfirm\":\"true\"}}]}").RootElement.Clone();
            var json = command switch { "status" => "{\"pipe\":\"connected\"}", "capabilities" => "{\"commands\":[{\"Command\":\"get_activity\"},{\"Command\":\"get_altering_works\"}]}", "get_activity" => "{}", _ => "{\"works\":[]}" };
            return JsonDocument.Parse(json).RootElement.Clone();
        }
        finally { Interlocked.Decrement(ref concurrent); }
    }
}

sealed class ActionFake : ICliTransport, ICollectionTransport
{
    public bool Missing { get; set; }
    public bool RequireApproval { get; set; }
    public int ActionCount { get; private set; }
    public Task<JsonElement> RunAsync(string path, string command, CancellationToken cancellationToken)
    {
        var text = command switch
        {
            "status" => "{\"pipe\":\"connected\"}",
            "capabilities" => "{\"commands\":[{\"Command\":\"complete_altering_work\",\"Metadata\":{\"requiresConfirm\":" + (RequireApproval ? "true" : "false") + "}},{\"Command\":\"get_music_scores\"},{\"Command\":\"play_music_score\"}]}",
            "get_music_scores" => Missing ? "[]" : "[{\"DisplayTitle\":\"가\"}]",
            _ => Missing ? "{\"works\":[]}" : "{\"works\":[{\"DisplayName\":\"철괴\",\"FacilityName\":\"대장간\",\"State\":\"Completed\"}]}"
        };
        return Task.FromResult(JsonDocument.Parse(text).RootElement.Clone());
    }
    public Task<JsonElement> CollectAsync(string path, string displayName) { ActionCount++; return Task.FromResult(JsonDocument.Parse("{\"collected\":1}").RootElement.Clone()); }
    public Task<JsonElement> PlayAsync(string path, string title) { ActionCount++; return Task.FromResult(JsonDocument.Parse("{\"result\":\"started\"}").RootElement.Clone()); }
}

