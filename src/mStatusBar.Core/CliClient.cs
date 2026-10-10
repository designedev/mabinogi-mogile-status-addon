using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace mStatusBar.Core;

public sealed class CliException(string message, int exitCode = 0, string? reason = null) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
    public string? Reason { get; } = reason;
}

public interface ICliTransport
{
    Task<JsonElement> RunAsync(string path, string command, CancellationToken cancellationToken);
}

// The process boundary is read-only. No body or arbitrary action command can be sent.
public interface ICollectionTransport
{
    Task<JsonElement> CollectAsync(string path, string displayName);
    Task<JsonElement> PlayAsync(string path, string title);
}

public sealed class CliProcess(IReadOnlyList<string>? prefixArguments = null) : ICliTransport, ICollectionTransport
{
    public static readonly IReadOnlySet<string> AllowedCommands = new HashSet<string>(StringComparer.Ordinal)
        { "status", "capabilities", "get_activity", "get_altering_works", "get_music_scores", "get_near_pcs", "get_current_environment", "get_my_info" };
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim collectionGate = new(1, 1);

    public async Task<JsonElement> RunAsync(string path, string command, CancellationToken cancellationToken)
    {
        if (!AllowedCommands.Contains(command)) throw new CliException("상태 조회 명령만 실행할 수 있습니다.");
        return await RunCoreAsync(path, command, null, gate, cancellationToken);
    }

    public Task<JsonElement> CollectAsync(string path, string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) throw new CliException("수령할 품목 이름이 필요합니다.");
        var json = JsonSerializer.Serialize(new { displayName }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var body = "base64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        // An action runs to its final game response. Settings changes/shutdown cannot kill it.
        return RunCoreAsync(path, "complete_altering_work", body, collectionGate, CancellationToken.None);
    }

    public Task<JsonElement> PlayAsync(string path, string title)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new CliException("악보 제목이 필요합니다.");
        var body = "base64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { title }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })));
        return RunCoreAsync(path, "play_music_score", body, collectionGate, CancellationToken.None);
    }

    private async Task<JsonElement> RunCoreAsync(string path, string command, string? body, SemaphoreSlim executionGate, CancellationToken cancellationToken)
    {
        await executionGate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path)) throw new CliException("CLI 파일을 찾을 수 없습니다. 설정에서 다시 선택해 주세요.");
            var start = new ProcessStartInfo(path)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!
            };
            foreach (var argument in prefixArguments ?? []) start.ArgumentList.Add(argument);
            start.ArgumentList.Add(command);
            if (body is not null) start.ArgumentList.Add(body);
            using var process = new Process { StartInfo = start };
            if (!process.Start()) throw new CliException("CLI를 시작하지 못했습니다.");
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            try { await process.WaitForExitAsync(cancellationToken); }
            catch (OperationCanceledException)
            {
                // Only read queries are possible here. Stop them on shutdown / settings changes.
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None);
                try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { }
                throw;
            }
            return Parse(await stdout, process.ExitCode, await stderr);
        }
        finally { executionGate.Release(); }
    }

    public static JsonElement Parse(string text, int exitCode, string stderr = "")
    {
        JsonElement response;
        try { response = JsonDocument.Parse(text.Trim().TrimStart('\uFEFF')).RootElement.Clone(); }
        catch (JsonException) { throw new CliException($"CLI 응답을 읽지 못했습니다 (종료 코드 {exitCode}). {stderr}", exitCode); }
        var body = JsonValue.Property(response, "body") ?? response;
        var reason = JsonValue.Text(response, "reason", JsonValue.Text(body, "reason"));
        var error = JsonValue.Text(response, "error", JsonValue.Text(body, "error"));
        var status = JsonValue.Text(response, "status");
        if (exitCode != 0 || status.Equals("rejected", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("invalid_body", StringComparison.OrdinalIgnoreCase) || error.Length > 0 ||
            JsonValue.Text(response, "pipe") == "disconnected" || JsonValue.Text(body, "pipe") == "disconnected")
        {
            var message = reason switch
            {
                "game_off" => "게임 클라이언트를 실행하거나 다시 시작해 주세요.",
                "option_off" => "게임 설정에서 MM AI 에이전트 활성화를 켜 주세요.",
                _ => JsonValue.Text(body, "message", JsonValue.Text(response, "message", error.Length > 0 ? error : $"CLI 오류 (코드 {exitCode})"))
            };
            throw new CliException(message, exitCode, reason);
        }
        return body;
    }
}

public static class JsonValue
{
    public static JsonElement? Property(JsonElement source, string name)
    {
        if (source.ValueKind != JsonValueKind.Object) return null;
        foreach (var item in source.EnumerateObject())
            if (item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return item.Value;
        return null;
    }
    public static string Text(JsonElement source, string name, string fallback = "") =>
        Property(source, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? fallback : fallback;
    public static bool Bool(JsonElement source, string name) => Property(source, name) is { } value &&
        (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.String && value.GetString() == "true");
    public static double Number(JsonElement source, string name) =>
        Property(source, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetDouble(out var result) && double.IsFinite(result) ? result : 0;
}

public sealed class ReadOnlyCliClient(ICliTransport transport, string cacheDirectory, string? sharedCachePath = null)
{
    private Dictionary<string, bool>? commands;
    private string? activePath;
    private readonly HashSet<string> optionalProbed = new(StringComparer.Ordinal);
    private void UsePath(string path)
    {
        if (activePath == path) return;
        activePath = path; commands = null; optionalProbed.Clear();
    }
    public async Task<JsonElement?> QueryNearbyAsync(string path, CancellationToken token)
        => await QueryOptionalAsync(path, "get_near_pcs", token);

    public async Task<JsonElement?> QueryEnvironmentAsync(string path, CancellationToken token)
        => await QueryOptionalAsync(path, "get_current_environment", token);

    public async Task<JsonElement?> QueryVitalsAsync(string path, CancellationToken token)
        => await QueryOptionalAsync(path, "get_my_info", token);

    private async Task<JsonElement?> QueryOptionalAsync(string path, string command, CancellationToken token)
    {
        UsePath(path);
        if (commands is null) await DiscoverAsync(path, false, token);
        // Older caches may lack optional queries. Refresh once rather than every poll.
        if (!commands!.ContainsKey(command) && optionalProbed.Add(command)) await DiscoverAsync(path, true, token);
        if (!commands!.ContainsKey(command)) return null;
        return await QueryAsync(path, command, token);
    }

    public async Task CheckStatusAsync(string path, CancellationToken token)
    {
        var status = await transport.RunAsync(path, "status", token);
        if (JsonValue.Text(status, "pipe") != "connected") throw new CliException("게임 연결 상태를 확인할 수 없습니다.");
    }

    public async Task<JsonElement> QueryAsync(string path, string command, CancellationToken token)
    {
        if (command is not ("get_activity" or "get_altering_works" or "get_music_scores" or "get_near_pcs" or "get_current_environment" or "get_my_info")) throw new CliException("지원하지 않는 상태 조회입니다.");
        UsePath(path);
        if (commands is null) await DiscoverAsync(path, false, token);
        if (!commands!.ContainsKey(command)) await DiscoverAsync(path, true, token);
        EnsureSupported(command);
        try { return await transport.RunAsync(path, command, token); }
        catch (CliException ex) when (ex.ExitCode == 4)
        {
            await DiscoverAsync(path, true, token);
            EnsureSupported(command);
            return await transport.RunAsync(path, command, token);
        }
    }

    private void EnsureSupported(string command)
    {
        if (!commands!.TryGetValue(command, out var confirmation)) throw new CliException($"게임이 {command} 조회를 지원하지 않습니다.");
        if (confirmation) throw new CliException($"{command} 조회에 승인이 필요한 스키마여서 자동 실행하지 않습니다.");
    }

    private async Task DiscoverAsync(string path, bool refresh, CancellationToken token)
    {
        // Bind our cache to the executable path, so choosing another CLI cannot reuse its schema.
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
        var cache = Path.Combine(cacheDirectory, key, "CAPABILITIES.json");
        var legacyCache = Path.Combine(cacheDirectory, key + ".json");
        var shared = sharedCachePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MabinogiMobileCLI", "CAPABILITIES.json");
        if (!refresh)
        {
            foreach (var file in new[] { cache, legacyCache, shared })
            {
                try
                {
                    if (!File.Exists(file)) continue;
                    commands = ParseCapabilities(JsonDocument.Parse(await File.ReadAllTextAsync(file, token)).RootElement);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or CliException) { }
            }
        }
        var response = await transport.RunAsync(path, "capabilities", token);
        commands = ParseCapabilities(response);
        try { SettingsStore.AtomicWrite(cache, response.GetRawText()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* In-memory schema still works. */ }
    }

    public static Dictionary<string, bool> ParseCapabilities(JsonElement source)
    {
        if (JsonValue.Bool(source, "loading")) throw new CliException("게임 입장 후 명령 목록을 다시 조회합니다.");
        if (JsonValue.Property(source, "body") is { } body) return ParseCapabilities(body);
        var entries = source.ValueKind == JsonValueKind.Array ? source : JsonValue.Property(source, "commands") ??
            JsonValue.Property(source, "capabilities") ?? throw new CliException("명령 목록을 읽지 못했습니다.");
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        var items = entries.ValueKind == JsonValueKind.Array ? entries.EnumerateArray().ToArray() :
            entries.ValueKind == JsonValueKind.Object ? entries.EnumerateObject().Select(p => p.Value).ToArray() : [];
        foreach (var entry in items)
        {
            var name = JsonValue.Text(entry, "command");
            if (name.Length == 0) continue;
            var metadata = JsonValue.Property(entry, "metadata") ?? entry;
            var confirm = JsonValue.Property(metadata, "requiresConfirm") ?? JsonValue.Property(entry, "requiresConfirm");
            if (confirm is { } c && c.ValueKind != JsonValueKind.True && c.ValueKind != JsonValueKind.False &&
                !(c.ValueKind == JsonValueKind.String && c.GetString() is "true" or "false"))
                throw new CliException("잘못된 승인 설정이 있는 명령 목록입니다.");
            result[name] = confirm is { } flag && (flag.ValueKind == JsonValueKind.True || flag.ValueKind == JsonValueKind.String && flag.GetString() == "true");
        }
        if (result.Count == 0) throw new CliException("명령 목록이 비어 있습니다.");
        return result;
    }
}
