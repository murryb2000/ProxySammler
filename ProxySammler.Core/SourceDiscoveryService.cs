using System.Security.Cryptography;
using System.Text;
using System.Net;

namespace ProxySammler.Core;

public sealed class SourceDiscoveryService
{
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly ISourceDiscoveryProvider _provider;
    private readonly SafeSourceDownloader _downloader;
    private readonly Func<IReadOnlyCollection<ProxyEndpoint>, int, TimeSpan, CancellationToken, Task<IReadOnlyList<ProxyTestResult>>> _check;

    public SourceDiscoveryService(ISourceDiscoveryProvider provider, SafeSourceDownloader downloader,
        Func<IReadOnlyCollection<ProxyEndpoint>, int, TimeSpan, CancellationToken, Task<IReadOnlyList<ProxyTestResult>>>? check = null)
    {
        _provider = provider;
        _downloader = downloader;
        _check = check ?? ((sample, bots, timeout, ct) => new ProxyChecker().CheckAllAsync(sample, bots, timeout, null, null, ct));
    }

    public bool IsRunning => _runGate.CurrentCount == 0;

    public async Task<DiscoverySummary?> SearchAsync(IList<SourceCandidate> candidates,
        IReadOnlyCollection<SourceDefinition> knownSources, bool autoAccept, int bots, TimeSpan timeout,
        Func<SourceCandidate, Task<bool>> accept, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (!await _runGate.WaitAsync(0, cancellationToken)) return null;
        try
        {
            var result = await _provider.SearchAsync(_downloader, progress, cancellationToken);
            var known = knownSources.Select(s => SourceFormats.NormalizeUrl(s.Url)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existing = candidates.Select(c => SourceFormats.NormalizeUrl(c.Url)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = new List<SourceCandidate>();
            var knownCount = 0;
            foreach (var link in result.Links)
            {
                string url;
                try { url = SourceFormats.NormalizeUrl(link.Url); }
                catch { continue; }
                if (known.Contains(url) || existing.Contains(url)) { knownCount++; continue; }
                var candidate = new SourceCandidate
                {
                    Url = url, Provider = link.Provider, Protocol = link.Protocol,
                    UpdatedUtc = link.UpdatedUtc
                };
                candidates.Add(candidate);
                added.Add(candidate);
                existing.Add(url);
            }

            var toCheck = added.Concat(candidates.Where(c => !added.Contains(c) &&
                (c.Status == CandidateStatus.Neu || c.Status == CandidateStatus.Fehlerhaft ||
                 (c.Status == CandidateStatus.Geprueft && c.WorkingCount == 0)) &&
                !known.Contains(SourceFormats.NormalizeUrl(c.Url)))).ToArray();
            var checkedCount = 0;
            var failed = 0;
            var accepted = 0;
            foreach (var candidate in toCheck)
            {
                var hashes = candidates.Where(c => !ReferenceEquals(c, candidate) && c.ContentHash.Length > 0)
                    .Select(c => c.ContentHash).ToHashSet(StringComparer.Ordinal);
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Quelle prüfen ({checkedCount + failed + 1}/{toCheck.Length}): {candidate.Url}");
                await CheckAsync(candidate, hashes, bots, timeout, cancellationToken);
                if (candidate.Status == CandidateStatus.Fehlerhaft) failed++;
                else checkedCount++;
                progress?.Report($"Quelle geprüft: {candidate.WorkingCount}/{candidate.SampleCount} Treffer, Qualität {candidate.Quality}/100 – {candidate.Url}");
                if (autoAccept && DiscoveryPolicy.Suitable(candidate) && await accept(candidate)) accepted++;
            }
            return new DiscoverySummary(result.Repositories, result.Links.Count, added.Count, knownCount, checkedCount, accepted, failed);
        }
        finally { _runGate.Release(); }
    }

    public async Task CheckAsync(SourceCandidate candidate, ISet<string> knownHashes, int bots,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        candidate.Reachable = false;
        candidate.ProxyCount = candidate.SampleCount = candidate.WorkingCount = 0;
        candidate.DuplicateContent = false;
        candidate.ContentHash = "";
        try
        {
            var download = await _downloader.GetAsync(candidate.Url, cancellationToken);
            candidate.Reachable = true;
            candidate.UpdatedUtc = download.LastModified ?? candidate.UpdatedUtc;
            var proxies = SourceFormats.Extract(download.Text, candidate.Protocol)
                .Where(p => IPAddress.TryParse(p.Host, out var address) && PublicUrlPolicy.IsPublic(address)).ToArray();
            candidate.ProxyCount = proxies.Length;
            if (proxies.Length == 0) throw new InvalidDataException("Keine öffentlichen Proxyadressen in unterstütztem Format gefunden.");
            if (proxies.All(p => p.Protocol == proxies[0].Protocol)) candidate.Protocol = proxies[0].Protocol;
            var normalized = string.Join('\n', proxies.OrderBy(p => p.Protocol).ThenBy(p => p.Host).ThenBy(p => p.Port)
                .Select(p => p.Url));
            candidate.ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
            candidate.DuplicateContent = knownHashes.Contains(candidate.ContentHash);
            knownHashes.Add(candidate.ContentHash);
            var sampleSize = Math.Min(proxies.Length, DiscoveryPolicy.MaxSampleTotalSize);
            var sampleOffset = candidate.SampleRound % proxies.Length;
            candidate.SampleRound = (candidate.SampleRound + 1) % proxies.Length;
            var sample = Enumerable.Range(0, sampleSize)
                .OrderBy(i => i % 3).ThenBy(i => i)
                .Select(i => proxies[(int)(((long)i * proxies.Length / sampleSize + sampleOffset) % proxies.Length)]).ToArray();
            foreach (var batch in sample.Chunk(DiscoveryPolicy.MaxSampleSize))
            {
                var results = await _check(batch, Math.Clamp(bots, 1, DiscoveryPolicy.MaxSampleSize), timeout, cancellationToken);
                candidate.SampleCount += results.Count;
                candidate.WorkingCount += results.Count(r => r.IsWorking);
                if (candidate.WorkingCount > 0) break;
            }
            candidate.Status = CandidateStatus.Geprueft;
            candidate.ErrorCount = 0;
            candidate.LastError = "";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            candidate.Status = CandidateStatus.Fehlerhaft;
            var message = "Abruf oder Prüfung fehlgeschlagen: " + ex.Message;
            candidate.LastError = message.Length > 200 ? message[..200] : message;
            candidate.ErrorCount++;
        }
        candidate.Quality = DiscoveryPolicy.Score(candidate, DateTimeOffset.UtcNow);
    }
}
