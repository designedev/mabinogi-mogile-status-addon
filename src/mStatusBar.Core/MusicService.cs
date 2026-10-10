using System.Globalization;
using System.Text.Json;
namespace mStatusBar.Core;
public static class MusicCatalog
{
    public static IReadOnlyList<string> Titles(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Array) throw new CliException("악보 목록 응답 형식이 올바르지 않습니다.");
        return data.EnumerateArray().Select(item => JsonValue.Text(item, "DisplayTitle")).Where(title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.Ordinal).OrderBy(title => title, StringComparer.Create(CultureInfo.GetCultureInfo("ko-KR"), false)).ThenBy(title => title, StringComparer.Ordinal).ToArray();
    }
}
public sealed class MusicService(ICliTransport queries, ICollectionTransport actions)
{
    public async Task<string> PlayAsync(string path, string title)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (JsonValue.Text(await queries.RunAsync(path, "status", default), "pipe") != "connected") throw new CliException("게임 연결을 확인해 주세요.");
            var schema = ReadOnlyCliClient.ParseCapabilities(await queries.RunAsync(path, "capabilities", default));
            if (!schema.ContainsKey("play_music_score")) throw new CliException("게임이 악보 연주를 지원하지 않습니다.");
            if (!schema.TryGetValue("get_music_scores", out var confirmation) || confirmation) throw new CliException("보유 악보 목록을 조회할 수 없습니다.");
            if (!MusicCatalog.Titles(await queries.RunAsync(path, "get_music_scores", default)).Contains(title, StringComparer.Ordinal)) throw new CliException("이 악보를 더 이상 보유하고 있지 않습니다.");
            try
            {
                // Human explicitly authorized immediate playback, including currency costs.
                return JsonValue.Text(await actions.PlayAsync(path, title), "message", $"「{title}」 연주를 시작했습니다.");
            }
            catch (CliException ex) when (ex.ExitCode == 4 && attempt == 0) { }
        }
        throw new CliException("연주를 시작하지 못했습니다.");
    }
}
