using System.Text.Json;

namespace ProxySammler.Core;

public interface ISourceDiscoveryProvider
{
    Task<(IReadOnlyList<DiscoveredLink> Links, int Repositories)> SearchAsync(
        SafeSourceDownloader downloader, IProgress<string>? progress, CancellationToken cancellationToken);
}

public sealed class GitHubSourceProvider : ISourceDiscoveryProvider
{
    private static readonly string[] Queries = ["proxy-list", "free-proxy-list", "socks5-proxy-list"];

    public async Task<(IReadOnlyList<DiscoveredLink> Links, int Repositories)> SearchAsync(
        SafeSourceDownloader downloader, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var repos = new Dictionary<string, (string Branch, DateTimeOffset? Updated)>(StringComparer.OrdinalIgnoreCase);
        var since = DateTimeOffset.UtcNow.AddMonths(-6).ToString("yyyy-MM-dd");
        foreach (var query in Queries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"GitHub-Repositories suchen: {query}");
            var q = Uri.EscapeDataString($"{query} in:name,description pushed:>{since}");
            var data = await downloader.GetAsync($"https://api.github.com/search/repositories?q={q}&sort=updated&order=desc&per_page=4", cancellationToken);
            using var json = JsonDocument.Parse(data.Text);
            foreach (var repo in json.RootElement.GetProperty("items").EnumerateArray())
            {
                var name = repo.GetProperty("full_name").GetString();
                var branch = repo.GetProperty("default_branch").GetString();
                if (name is null || branch is null || repos.ContainsKey(name)) continue;
                DateTimeOffset? updated = repo.GetProperty("pushed_at").TryGetDateTimeOffset(out var date) ? date : null;
                repos[name] = (branch, updated);
            }
        }

        var links = new List<DiscoveredLink>();
        foreach (var (name, meta) in repos.Take(12))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Repository untersuchen: {name}");
            try
            {
                var tree = await downloader.GetAsync(
                    $"https://api.github.com/repos/{name}/git/trees/{Uri.EscapeDataString(meta.Branch)}?recursive=1", cancellationToken);
                using var json = JsonDocument.Parse(tree.Text);
                if (json.RootElement.TryGetProperty("tree", out var entries))
                {
                    foreach (var entry in entries.EnumerateArray())
                    {
                        if (entry.GetProperty("type").GetString() != "blob") continue;
                        var path = entry.GetProperty("path").GetString();
                        if (path is null || !SourceFormats.LooksLikeList("https://raw.githubusercontent.com/" + name + "/" + meta.Branch + "/" + path)) continue;
                        var escapedPath = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
                        var url = $"https://raw.githubusercontent.com/{name}/{Uri.EscapeDataString(meta.Branch)}/{escapedPath}";
                        links.Add(new DiscoveredLink(url, "GitHub: " + name, SourceFormats.InferProtocol(path), meta.Updated));
                    }
                }
                var readme = await downloader.GetAsync(
                    $"https://raw.githubusercontent.com/{name}/{Uri.EscapeDataString(meta.Branch)}/README.md", cancellationToken);
                links.AddRange(SourceFormats.ExtractReadmeLinks(readme.Text, "GitHub: " + name, meta.Updated));
            }
            catch (RateLimitException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { progress?.Report($"{name}: {ex.Message}"); }
        }
        return (links.DistinctBy(x => x.Url, StringComparer.OrdinalIgnoreCase).Take(100).ToArray(), repos.Count);
    }
}
