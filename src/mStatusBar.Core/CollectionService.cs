using System.Text.Json;

namespace mStatusBar.Core;

public sealed record CollectionConfirmation(string Facility, string Item, string Description, string Note);

public sealed class CollectionService(ICliTransport queries, ICollectionTransport actions)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<string> CollectAsync(string path, WorkState selected, Func<CollectionConfirmation, Task<bool>> approve)
    {
        if (!selected.Completed || string.IsNullOrWhiteSpace(selected.Name)) throw new CliException("완료된 작업만 수령할 수 있습니다.");
        if (!await gate.WaitAsync(0)) throw new CliException("이미 수령 작업이 진행 중입니다.");
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var status = await queries.RunAsync(path, "status", default);
                if (JsonValue.Text(status, "pipe") != "connected") throw new CliException("게임 연결을 확인해 주세요.");
                var schema = await queries.RunAsync(path, "capabilities", default);
                var supported = ReadOnlyCliClient.ParseCapabilities(schema);
                if (!supported.TryGetValue("complete_altering_work", out var requiresConfirm)) throw new CliException("게임이 완료품 수령을 지원하지 않습니다.");
                var entry = FindCollection(schema);
                var metadata = JsonValue.Property(entry, "metadata") ?? entry;
                var note = JsonValue.Text(metadata, "note", JsonValue.Text(entry, "note"));
                if (requiresConfirm || note.Contains("consumes", StringComparison.OrdinalIgnoreCase))
                {
                    if (!await approve(new(selected.Facility, selected.Name, JsonValue.Text(entry, "description"), note)))
                        return "수령을 취소했습니다.";
                }
                var queue = WorkQueue.Parse(await queries.RunAsync(path, "get_altering_works", default));
                if (!queue.Works.Any(w => w.Completed && w.Name == selected.Name && w.Facility == selected.Facility))
                    throw new CliException("해당 완료품이 더 이상 없습니다. 최신 목록을 확인해 주세요.");
                try
                {
                    var response = await actions.CollectAsync(path, selected.Name);
                    if (JsonValue.Text(response, "result") == "stopped_by_user") return "게임에서 수령 이동을 중단했습니다.";
                    return JsonValue.Text(response, "message", $"{selected.Facility} 수령 작업이 끝났습니다. 최신 목록을 확인합니다.");
                }
                catch (CliException ex) when (ex.ExitCode == 4 && attempt == 0) { /* Refresh schema and approval before one retry. */ }
            }
            throw new CliException("수령 명령을 실행하지 못했습니다.");
        }
        finally { gate.Release(); }
    }

    private static JsonElement FindCollection(JsonElement schema)
    {
        if (JsonValue.Property(schema, "body") is { } body) return FindCollection(body);
        var entries = schema.ValueKind == JsonValueKind.Array ? schema : JsonValue.Property(schema, "commands") ?? JsonValue.Property(schema, "capabilities") ?? default;
        var items = entries.ValueKind == JsonValueKind.Array ? entries.EnumerateArray().ToArray() : entries.ValueKind == JsonValueKind.Object ? entries.EnumerateObject().Select(p => p.Value).ToArray() : [];
        return items.First(item => JsonValue.Text(item, "command") == "complete_altering_work");
    }
}
