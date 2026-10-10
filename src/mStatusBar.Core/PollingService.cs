namespace mStatusBar.Core;

public sealed class PollingService(ReadOnlyCliClient client) : IAsyncDisposable
{
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private CancellationTokenSource? cancellation;
    private Task? loop;
    public event Action<StatusSnapshot>? Updated;

    public async Task ConfigureAsync(AppSettings settings)
    {
        settings.Validate();
        await lifecycle.WaitAsync();
        try
        {
            await StopCoreAsync();
            cancellation = new();
            loop = PollAsync(settings, cancellation.Token);
        }
        finally { lifecycle.Release(); }
    }

    public static TimeSpan Delay(int seconds, int failures) => TimeSpan.FromSeconds(
        failures == 0 ? seconds : Math.Min(60, seconds * Math.Pow(2, Math.Min(5, failures))));

    private async Task PollAsync(AppSettings settings, CancellationToken token)
    {
        var failures = 0;
        while (!token.IsCancellationRequested)
        {
            StatusSnapshot snapshot;
            try
            {
                if (string.IsNullOrEmpty(settings.CliPath))
                    snapshot = StatusSnapshot.Unavailable("설정에서 CLI 파일을 선택해 주세요.", TimeSpan.FromSeconds(settings.PollIntervalSeconds));
                else
                {
                    await client.CheckStatusAsync(settings.CliPath, token);
                    ActivityState? activity = null;
                    WorkQueue? queue = null;
                    NearbyPerformance? nearbyMusic = null;
                    string? nearbyUnavailable = null;
                    EnvironmentState? environment = null;
                    string? environmentUnavailable = null;
                    HealthState? health = null;
                    string? healthUnavailable = null;
                    var errors = new List<string>();
                    var activityAt = DateTimeOffset.Now;
                    try
                    {
                        activity = ActivityState.Parse(await client.QueryAsync(settings.CliPath, "get_activity", token));
                        activityAt = DateTimeOffset.Now;
                    }
                    catch (CliException ex) when (ex.ExitCode == 5 || ex.Reason is "game_off" or "option_off") { throw; }
                    catch (Exception ex) when (ex is not OperationCanceledException) { errors.Add("행동: " + ex.Message); }
                    try { queue = WorkQueue.Parse(await client.QueryAsync(settings.CliPath, "get_altering_works", token)); }
                    catch (CliException ex) when (ex.ExitCode == 5 || ex.Reason is "game_off" or "option_off") { throw; }
                    catch (Exception ex) when (ex is not OperationCanceledException) { errors.Add("작업 큐: " + ex.Message); }
                    try
                    {
                        var response = await client.QueryEnvironmentAsync(settings.CliPath, token);
                        if (response is { } value) environment = EnvironmentState.Parse(value);
                        else environmentUnavailable = "게임이 현재 환경 조회를 지원하지 않습니다.";
                    }
                    catch (CliException ex) when (ex.ExitCode == 5 || ex.Reason is "game_off" or "option_off") { throw; }
                    catch (Exception ex) when (ex is not OperationCanceledException) { environmentUnavailable = ex.Message; }
                    try
                    {
                        var response = await client.QueryVitalsAsync(settings.CliPath, token);
                        if (response is { } value) health = HealthState.Parse(value);
                        else healthUnavailable = "게임이 생명력 조회를 지원하지 않습니다.";
                    }
                    catch (CliException ex) when (ex.ExitCode == 5 || ex.Reason is "game_off" or "option_off") { throw; }
                    catch (Exception ex) when (ex is not OperationCanceledException) { healthUnavailable = ex.Message; }
                    if (activity is { Music.IsPlaying: false })
                    {
                        try
                        {
                            var nearby = await client.QueryNearbyAsync(settings.CliPath, token);
                            if (nearby is { } response) nearbyMusic = NearbyPerformance.ParseNearest(response);
                            else nearbyUnavailable = "게임이 주변 연주 조회를 지원하지 않습니다.";
                        }
                        catch (CliException ex) when (ex.ExitCode == 5 || ex.Reason is "game_off" or "option_off") { throw; }
                        catch (Exception ex) when (ex is not OperationCanceledException) { nearbyUnavailable = ex.Message; }
                    }
                    failures = errors.Count == 0 ? 0 : failures + 1;
                    snapshot = new(true, errors.Count == 0 ? "게임 연결됨" : "일부 상태를 조회하지 못했습니다.",
                        activity, queue, activityAt, Delay(settings.PollIntervalSeconds, failures), errors)
                        { NearbyMusic = nearbyMusic, NearbyMusicUnavailableReason = nearbyUnavailable,
                            Environment = environment, EnvironmentUnavailableReason = environmentUnavailable,
                            Health = health, HealthUnavailableReason = healthUnavailable };
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                snapshot = StatusSnapshot.Unavailable(ex.Message, Delay(settings.PollIntervalSeconds, ++failures));
            }
            if (token.IsCancellationRequested) break;
            Updated?.Invoke(snapshot);
            // Wait after completing the whole batch; slow queries never accumulate.
            try { await Task.Delay(snapshot.NextPoll, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
        }
    }

    private async Task StopCoreAsync()
    {
        if (cancellation is null) return;
        await cancellation.CancelAsync();
        if (loop is not null) await loop;
        cancellation.Dispose();
        cancellation = null;
        loop = null;
    }

    public async ValueTask DisposeAsync()
    {
        await lifecycle.WaitAsync();
        try { await StopCoreAsync(); }
        finally { lifecycle.Release(); }
    }
}

