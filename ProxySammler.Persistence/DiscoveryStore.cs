using System.Text;
using System.Text.Json;
using ProxySammler.Core;

namespace ProxySammler.Persistence;

public static class AtomicTextFile
{
    public static void Write(string path, string text, bool createBackup = true)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                var backup = createBackup ? path + ".bak" : null;
                File.Replace(temp, path, backup, ignoreMetadataErrors: true);
            }
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed class DiscoveryStore
{
    private readonly string _directory;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public DiscoveryStore(string directory) => _directory = directory;
    public string CandidatesPath => Path.Combine(_directory, "source_candidates.json");
    public string SettingsPath => Path.Combine(_directory, "source_discovery_settings.json");

    public List<SourceCandidate> LoadCandidates() => Read(CandidatesPath, new List<SourceCandidate>());
    public DiscoverySettings LoadSettings() => Read(SettingsPath, new DiscoverySettings());
    public void SaveCandidates(IReadOnlyCollection<SourceCandidate> candidates) =>
        AtomicTextFile.Write(CandidatesPath, JsonSerializer.Serialize(candidates, JsonOptions));
    public void SaveSettings(DiscoverySettings settings) =>
        AtomicTextFile.Write(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));

    private static T Read<T>(string path, T fallback)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? fallback : fallback; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return fallback; }
    }
}

public static class SourceRegistry
{
    public static bool Add(string path, SourceCandidate candidate)
    {
        var normalized = SourceFormats.NormalizeUrl(candidate.Url);
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        foreach (var line in lines)
        {
            var parts = line.Trim().Split('|', 2);
            if (parts.Length != 2) continue;
            try
            {
                if (SourceFormats.NormalizeUrl(parts[1]).Equals(normalized, StringComparison.OrdinalIgnoreCase)) return false;
            }
            catch { }
        }
        var protocol = candidate.Protocol switch
        {
            ProxyProtocol.Socks4 => "socks4",
            ProxyProtocol.Socks5 => "socks5",
            _ => "http"
        };
        var original = File.Exists(path) ? File.ReadAllText(path) : "";
        var separator = original.Contains("\r\n") ? "\r\n" : "\n";
        var updated = original + (original.Length > 0 && !original.EndsWith('\n') ? separator : "") +
            protocol + "|" + normalized + separator;
        AtomicTextFile.Write(path, updated);
        return true;
    }
}

public sealed class SourcePerformanceStore
{
    public sealed class Entry
    {
        public string Url { get; set; } = "";
        public int Runs { get; set; }
        public int RunsWithoutNew { get; set; }
        public int LastWorking { get; set; }
        public int LastNew { get; set; }
        public int TotalNew { get; set; }
        public DateTime LastCheckedUtc { get; set; }
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<Entry> Entries => _entries.Values.ToArray();

    public static SourcePerformanceStore Load(string path)
    {
        var result = new SourcePerformanceStore();
        try
        {
            if (File.Exists(path))
                foreach (var entry in JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path)) ?? [])
                    if (!string.IsNullOrWhiteSpace(entry.Url)) result._entries[entry.Url] = entry;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return result;
    }

    public bool ShouldFetch(string url, DateTime now) =>
        !_entries.TryGetValue(url, out var entry) || entry.RunsWithoutNew < 3 ||
        now - entry.LastCheckedUtc >= TimeSpan.FromHours(6);

    public void Record(string url, int working, int newWorking, DateTime now)
    {
        if (!_entries.TryGetValue(url, out var entry)) _entries[url] = entry = new Entry { Url = url };
        entry.Runs++;
        entry.LastWorking = working;
        entry.LastNew = newWorking;
        entry.TotalNew += newWorking;
        entry.RunsWithoutNew = newWorking == 0 ? Math.Min(3, entry.RunsWithoutNew + 1) : 0;
        entry.LastCheckedUtc = now;
    }

    public void Save(string path) =>
        AtomicTextFile.Write(path, JsonSerializer.Serialize(_entries.Values.OrderBy(x => x.Url), new JsonSerializerOptions { WriteIndented = true }));
}
