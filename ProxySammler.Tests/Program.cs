using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ProxySammler.Core;
using ProxySammler.Persistence;

var failures = new List<string>();
void Assert(bool condition, string message) { if (!condition) failures.Add(message); }

var temp = Path.Combine(Path.GetTempPath(), "ProxySammler-1.8.1-Test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var store = ProxyListStore.Load(temp);
    store.Save(temp);
    var expected = new[] { "HTTP.txt", "SOCKS4.txt", "SOCKS5.txt" };
    Assert(Directory.GetFiles(temp, "*.txt").Select(Path.GetFileName).OrderBy(x => x)
        .SequenceEqual(expected.OrderBy(x => x)), "Es dürfen nur die drei gewünschten Listen entstehen.");

    var http = new ProxyTestResult
    {
        Proxy = new ProxyEndpoint(ProxyProtocol.Http, "1.2.3.4", 8080), IsWorking = true, ResponseTimeMs = 100
    };
    var socks5 = new ProxyTestResult
    {
        Proxy = new ProxyEndpoint(ProxyProtocol.Socks5, "5.6.7.8", 1080), IsWorking = true, ResponseTimeMs = 200
    };
    Assert(store.AddWorking(new[] { http, socks5 }) == 2, "Neue funktionierende Proxys wurden nicht angehängt.");
    Assert(store.AddWorking(new[] { http }) == 0, "Doppelte Proxys dürfen nicht erneut angehängt werden.");
    store.Save(temp);
    Assert(File.ReadAllText(Path.Combine(temp, "HTTP.txt")).Trim() == "1.2.3.4:8080", "HTTP-Liste hat falsches Format.");
    Assert(File.ReadAllText(Path.Combine(temp, "SOCKS5.txt")).Trim() == "5.6.7.8:1080", "SOCKS5-Liste hat falsches Format.");

    var reloaded = ProxyListStore.Load(temp);
    var deadHttp = new ProxyTestResult { Proxy = http.Proxy, IsWorking = false, Error = "Zeitüberschreitung" };
    var removed = reloaded.ReplaceSelected(new HashSet<ProxyProtocol> { ProxyProtocol.Http }, new[] { deadHttp });
    reloaded.Save(temp);
    Assert(removed == 1 && File.ReadAllText(Path.Combine(temp, "HTTP.txt")).Length == 0,
        "Ein toter Proxy muss nach dem Nachtest entfernt werden.");
    Assert(File.ReadAllText(Path.Combine(temp, "SOCKS5.txt")).Contains("5.6.7.8:1080"),
        "Nicht ausgewählte Listen müssen unverändert bleiben.");

    var migrationRoot = Path.Combine(temp, "migration");
    var results = Path.Combine(migrationRoot, "Ergebnisse");
    var legacy = Path.Combine(migrationRoot, "Gefundene_Proxys");
    Directory.CreateDirectory(legacy);
    File.WriteAllText(Path.Combine(legacy, "gefundene_http.txt"), "9.9.9.9:3128\n");
    var migrated = ProxyListStore.Load(results, legacy);
    migrated.Save(results);
    Assert(File.ReadAllText(Path.Combine(results, "HTTP.txt")).Contains("9.9.9.9:3128"),
        "Eine vorhandene Trefferliste aus Version 1.5 sollte übernommen werden.");

    var extracted = SourceFormats.Extract("1.2.3.4:80\nhttp://1.2.3.4:80\nsocks4a://2.3.4.5:1080\nsocks5h://3.4.5.6:1080", ProxyProtocol.Http);
    Assert(extracted.Count == 3 && extracted.Count(p => p.Protocol == ProxyProtocol.Http) == 1 &&
        extracted.Any(p => p.Protocol == ProxyProtocol.Socks4) && extracted.Any(p => p.Protocol == ProxyProtocol.Socks5),
        "HTTP-, SOCKS4- und SOCKS5-Adressen müssen erkannt und dedupliziert werden.");
    Assert(SourceFormats.InferProtocol("https://example.com/socks5.txt") == ProxyProtocol.Socks5 &&
        SourceFormats.InferProtocol("https://example.com/socks4.csv") == ProxyProtocol.Socks4,
        "Der Dateiname muss den vermuteten Proxytyp liefern.");
    Assert(SourceFormats.Extract("ip,port,protocol\n4.4.4.4,1080,socks5", ProxyProtocol.Http)
        .Single().Protocol == ProxyProtocol.Socks5, "CSV-Spalten müssen als Proxy erkannt werden.");
    Assert(SourceFormats.Extract("{\"data\":[{\"ip\":\"5.5.5.5\",\"port\":1080,\"type\":\"socks4\"}]}", ProxyProtocol.Http)
        .Single().Protocol == ProxyProtocol.Socks4, "JSON-Objekte müssen als Proxy erkannt werden.");
    Assert(SourceFormats.NormalizeUrl("HTTPS://EXAMPLE.COM/list.txt") ==
        SourceFormats.NormalizeUrl("https://example.com/list.txt"), "URL-Normalisierung muss Duplikate erkennen.");

    // Exercise the actual collection path after a discovered source is registered.
    using (var client = new HttpClient(new ReplyHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("csv")
            ? "ip,port,protocol\n8.8.8.8,8080,http\n9.9.9.9,1080,socks5"
            : "[{\"ip\":\"1.1.1.1\",\"port\":1080,\"type\":\"socks4\"}]")
    })))
    {
        var collector = new ProxyCollector(client);
        var all = new HashSet<ProxyProtocol> { ProxyProtocol.Http, ProxyProtocol.Socks4, ProxyProtocol.Socks5 };
        var sources = new[] { new SourceDefinition(ProxyProtocol.Http, "https://example.com/proxies.csv"),
            new SourceDefinition(ProxyProtocol.Http, "https://example.com/proxies.json") };
        var collection = await collector.CollectAsync(sources, all, null, CancellationToken.None);
        Assert(collection.Proxies.Count == 3 && collection.Sources.All(x => x.Error is null),
            "Übernommene CSV-/JSON-Quellen müssen in der normalen Suche Kandidaten liefern.");
        var socksOnly = await collector.CollectAsync(sources, new HashSet<ProxyProtocol> { ProxyProtocol.Socks5 }, null, CancellationToken.None);
        Assert(socksOnly.Proxies.Count == 1 && socksOnly.Proxies.Single().Protocol == ProxyProtocol.Socks5,
            "Gemischte Quellen müssen auch bei abweichendem Quellentyp nur ausgewählte Protokolle liefern.");
    }

    var retryHistory = new ProxyHistory();
    var recentDead = new ProxyEndpoint(ProxyProtocol.Http, "8.8.8.8", 80);
    var oldDead = new ProxyEndpoint(ProxyProtocol.Http, "9.9.9.9", 80);
    var knownWorking = new ProxyEndpoint(ProxyProtocol.Http, "1.1.1.1", 80);
    retryHistory.Update(new[] {
        new ProxyTestResult { Proxy = recentDead, IsWorking = false, CheckedAtUtc = DateTime.UtcNow },
        new ProxyTestResult { Proxy = oldDead, IsWorking = false, CheckedAtUtc = DateTime.UtcNow.AddHours(-7) },
        new ProxyTestResult { Proxy = knownWorking, IsWorking = true, CheckedAtUtc = DateTime.UtcNow }
    });
    var retryList = new[] { recentDead, oldDead, knownWorking };
    var retry = retryHistory.SelectNew(retryList, out var retrySkipped);
    Assert(retry.SequenceEqual(new[] { oldDead }) && retrySkipped == 2,
        "Neue Suche muss frühere Fehler nach sechs Stunden erneut prüfen, bekannte Treffer nachprüfen lassen.");
    retry = retryHistory.SelectNew(retryList, out retrySkipped, skipRecentFailures: false);
    Assert(retry.Count == 2 && retry.Contains(recentDead) && retry.Contains(oldDead) && retrySkipped == 1,
        "Ohne Negativcache müssen auch frisch ausgefallene Proxys erneut geprüft werden.");

    var candidate = new SourceCandidate { Url = "https://example.com/list.txt", Protocol = ProxyProtocol.Http,
        Status = CandidateStatus.Geprueft, Reachable = true, ProxyCount = 100, SampleCount = 12,
        WorkingCount = 1, UpdatedUtc = DateTimeOffset.UtcNow };
    candidate.Quality = DiscoveryPolicy.Score(candidate, DateTimeOffset.UtcNow);
    Assert(DiscoveryPolicy.Suitable(candidate), "Ein einzelner bestätigter Treffer darf trotz niedriger Quote geeignet sein.");
    candidate.DuplicateContent = true;
    Assert(DiscoveryPolicy.Score(candidate, DateTimeOffset.UtcNow) < 45,
        "Doppelte Inhalte müssen die Bewertung sichtbar senken.");

    var discovery = new DiscoveryStore(temp);
    File.WriteAllText(discovery.CandidatesPath, "{kaputt");
    File.WriteAllText(discovery.SettingsPath, "{kaputt");
    Assert(discovery.LoadCandidates().Count == 0 && !discovery.LoadSettings().AutoAcceptGood,
        "Beschädigte JSON-Dateien dürfen den Start nicht verhindern.");
    discovery.SaveCandidates([candidate]);
    Assert(discovery.LoadCandidates().Count == 1 && File.Exists(discovery.CandidatesPath + ".bak"),
        "Kandidaten müssen atomar mit Sicherung gespeichert werden.");
    var settings = new DiscoverySettings { LastSearchUtc = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero) };
    Assert(settings.NextSearchUtc(DateTimeOffset.UtcNow) == settings.LastSearchUtc.Value.AddDays(1),
        "Tägliches Intervall ist falsch.");
    settings.Interval = DiscoveryInterval.Woechentlich;
    Assert(settings.NextSearchUtc(DateTimeOffset.UtcNow) == settings.LastSearchUtc.Value.AddDays(7),
        "Wöchentliches Intervall ist falsch.");

    var sourcePath = Path.Combine(temp, "sources.txt");
    var manualLine = "# Manuell\nhttp|https://manual.example/proxy.txt\n";
    File.WriteAllText(sourcePath, manualLine);
    candidate.DuplicateContent = false;
    Assert(SourceRegistry.Add(sourcePath, candidate), "Geprüfte Quelle konnte nicht übernommen werden.");
    Assert(!SourceRegistry.Add(sourcePath, candidate), "Doppelte Quelle wurde übernommen.");
    Assert(File.ReadAllText(sourcePath).StartsWith(manualLine) &&
        File.ReadAllText(sourcePath + ".bak") == manualLine,
        "Manuelle Quellen müssen erhalten und vor Änderungen gesichert werden.");

    var resolver = new PublicResolver();
    Assert(!PublicUrlPolicy.IsPublic(IPAddress.Parse("127.0.0.1")) &&
        !PublicUrlPolicy.IsPublic(IPAddress.Parse("10.0.0.1")) &&
        !PublicUrlPolicy.IsPublic(IPAddress.Parse("169.254.169.254")) &&
        !PublicUrlPolicy.IsPublic(IPAddress.Parse("192.168.1.1")),
        "Lokale, private und Metadaten-Adressen müssen gesperrt sein.");
    var rejected = false;
    try { await PublicUrlPolicy.ValidateAsync(new Uri("http://localhost/list.txt"), resolver, CancellationToken.None); }
    catch (InvalidDataException) { rejected = true; }
    Assert(rejected, "localhost muss vor dem Abruf gesperrt sein.");
    var dualStack = await PublicUrlPolicy.ValidateAsync(new Uri("https://public.example/list.txt"),
        new FixedResolver(IPAddress.Parse("2606:4700:4700::1111"), IPAddress.Parse("8.8.8.8")), CancellationToken.None);
    Assert(dualStack.Length == 1 && dualStack[0].ToString() == "8.8.8.8",
        "Dual-Stack-DNS muss sichere IPv4-Adressen verwenden können.");
    rejected = false;
    try { await PublicUrlPolicy.ValidateAsync(new Uri("https://private.example/list.txt"),
        new FixedResolver(IPAddress.Parse("8.8.8.8"), IPAddress.Parse("10.0.0.1")), CancellationToken.None); }
    catch (InvalidDataException) { rejected = true; }
    Assert(rejected, "DNS-Antworten mit privaten IPv4-Adressen müssen gesperrt werden.");

    using (var downloader = new SafeSourceDownloader(resolver, new ReplyHandler(_ =>
        new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("http://127.0.0.1/secret") } })))
    {
        rejected = false;
        try { await downloader.GetAsync("https://public.example/list.txt", CancellationToken.None); }
        catch (InvalidDataException) { rejected = true; }
        Assert(rejected, "Weiterleitungen auf interne Adressen müssen blockiert werden.");
    }
    using (var downloader = new SafeSourceDownloader(resolver, new ReplyHandler(_ =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds().ToString());
        return response;
    })))
    {
        rejected = false;
        try { await downloader.GetAsync("https://api.github.com/search/repositories", CancellationToken.None); }
        catch (RateLimitException ex) { rejected = ex.RetryAt > DateTimeOffset.UtcNow; }
        Assert(rejected, "GitHub-Ratenbegrenzung und Wartezeit müssen erkannt werden.");
    }

    using (var downloader = new SafeSourceDownloader(resolver, new ReplyHandler(request =>
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = path.Contains("/search/repositories")
            ? "{\"items\":[{\"full_name\":\"example/proxy-list\",\"default_branch\":\"main\",\"pushed_at\":\"2026-10-01T00:00:00Z\"}]}"
            : path.Contains("/git/trees/")
                ? "{\"tree\":[{\"type\":\"blob\",\"path\":\"proxies/socks5.txt\"}]}"
                : "Liste: https://example.com/http-proxy-list.csv";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
    })))
    {
        var found = await new GitHubSourceProvider().SearchAsync(downloader, null, CancellationToken.None);
        Assert(found.Repositories == 1 && found.Links.Any(x => x.Protocol == ProxyProtocol.Socks5) &&
            found.Links.Any(x => x.Url.Contains("http-proxy-list.csv")),
            "GitHub-Suche muss Rohdateien und README-Links finden.");
    }

    IReadOnlyCollection<ProxyEndpoint>? sampled = null;
    using (var downloader = new SafeSourceDownloader(resolver, new ReplyHandler(_ =>
        new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("127.0.0.1:80\n8.8.8.8:8080") })))
    {
        var service = new SourceDiscoveryService(new BlockingProvider(), downloader,
            (proxies, _, _, _) =>
            {
                sampled = proxies;
                return Task.FromResult<IReadOnlyList<ProxyTestResult>>(proxies.Select(p =>
                    new ProxyTestResult { Proxy = p, IsWorking = true }).ToArray());
            });
        var checkedCandidate = new SourceCandidate { Url = "https://public.example/proxies.txt" };
        await service.CheckAsync(checkedCandidate, new HashSet<string>(), 1, TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert(sampled?.Count == 1 && sampled.Single().Host == "8.8.8.8" && checkedCandidate.ProxyCount == 1,
            "Private Proxyziele dürfen in automatisch entdeckten Listen nicht geprüft werden.");
    }

    // A working proxy outside the first batch must still be found.
    var sampleCalls = 0;
    var listText = string.Join('\n', Enumerable.Range(1, 100).Select(i => $"8.8.8.{i}:8080"));
    using (var downloader = new SafeSourceDownloader(resolver, new ReplyHandler(_ =>
        new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(listText) })))
    {
        var service = new SourceDiscoveryService(new BlockingProvider(), downloader, (proxies, _, _, _) =>
        {
            sampleCalls++;
            Assert(proxies.Count <= DiscoveryPolicy.MaxSampleSize, "Stichproben-Batches dürfen maximal zwölf Proxys umfassen.");
            return Task.FromResult<IReadOnlyList<ProxyTestResult>>(proxies.Select(p => new ProxyTestResult
                { Proxy = p, IsWorking = p.Host == "8.8.8.3" }).ToArray());
        });
        var checkedCandidate = new SourceCandidate { Url = "https://public.example/proxies.txt" };
        await service.CheckAsync(checkedCandidate, new HashSet<string>(), 1, TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert(sampleCalls == 2 && checkedCandidate.SampleCount == 24 && checkedCandidate.WorkingCount == 1,
            "Nach zwölf erfolglosen Stichproben muss ein weiterer Teil der Liste geprüft werden.");
    }
    using (var downloader = new SafeSourceDownloader(resolver, new ReplyHandler(_ =>
        new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(listText) })))
    {
        var sampledHosts = new List<string>();
        var service = new SourceDiscoveryService(new BlockingProvider(), downloader, (proxies, _, _, _) =>
        {
            sampledHosts.AddRange(proxies.Select(p => p.Host));
            return Task.FromResult<IReadOnlyList<ProxyTestResult>>(proxies.Select(p => new ProxyTestResult
                { Proxy = p, IsWorking = false }).ToArray());
        });
        var checkedCandidate = new SourceCandidate { Url = "https://public.example/proxies.txt" };
        await service.CheckAsync(checkedCandidate, new HashSet<string>(), 1, TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert(checkedCandidate.SampleCount == 36 && checkedCandidate.WorkingCount == 0,
            "Erweiterte Stichproben müssen bei 36 enden.");
        var firstHosts = sampledHosts.ToHashSet();
        Assert(firstHosts.Count == 36, "Erweiterte Stichproben müssen unterschiedliche Adressen prüfen.");
        sampledHosts.Clear();
        await service.CheckAsync(checkedCandidate, new HashSet<string>(), 1, TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert(!firstHosts.SetEquals(sampledHosts), "Wiederholte Stichproben müssen ihre Auswahl verschieben.");
    }
    // Existing failed candidates must recover on a later search without self-duplicate penalties.
    using (var downloader = new SafeSourceDownloader(resolver, new ReplyHandler(_ =>
        new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("8.8.8.8:8080") })))
    {
        var service = new SourceDiscoveryService(new EmptyProvider(), downloader, (proxies, _, _, _) =>
            Task.FromResult<IReadOnlyList<ProxyTestResult>>(proxies.Select(p => new ProxyTestResult
                { Proxy = p, IsWorking = true }).ToArray()));
        var recovering = new SourceCandidate { Url = "https://public.example/proxies.txt", Status = CandidateStatus.Fehlerhaft,
            Reachable = true, ProxyCount = 99, SampleCount = 12, WorkingCount = 10, ContentHash = "old", ErrorCount = 3 };
        var summary = await service.SearchAsync(new List<SourceCandidate> { recovering }, [], false, 1,
            TimeSpan.FromSeconds(1), _ => Task.FromResult(false), null, CancellationToken.None);
        Assert(summary?.Checked == 1 && recovering.Status == CandidateStatus.Geprueft &&
            recovering.SampleCount == 1 && recovering.WorkingCount == 1 && !recovering.DuplicateContent && recovering.ErrorCount == 0,
            "Fehlerhafte Kandidaten müssen erneut geprüft werden und veraltete Prüfdaten verlieren.");
    }
    using (var downloader = new SafeSourceDownloader(resolver, new ReplyHandler(_ =>
        new HttpResponseMessage(HttpStatusCode.NotFound))))
    {
        var service = new SourceDiscoveryService(new EmptyProvider(), downloader);
        var stale = new SourceCandidate { Url = "https://public.example/proxies.txt", Reachable = true,
            ProxyCount = 100, SampleCount = 12, WorkingCount = 12, ContentHash = "old" };
        await service.CheckAsync(stale, new HashSet<string>(), 1, TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert(!stale.Reachable && stale.ProxyCount == 0 && stale.WorkingCount == 0 && stale.ContentHash == "" &&
            stale.Status == CandidateStatus.Fehlerhaft, "Abruffehler dürfen keine alten Trefferzahlen anzeigen.");
    }

    var gateProvider = new BlockingProvider();
    using (var downloader = new SafeSourceDownloader(resolver, new ReplyHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))))
    {
        var service = new SourceDiscoveryService(gateProvider, downloader);
        var first = service.SearchAsync(new List<SourceCandidate>(), [], false, 1, TimeSpan.FromSeconds(1),
            _ => Task.FromResult(false), null, CancellationToken.None);
        await gateProvider.Entered.Task;
        var second = await service.SearchAsync(new List<SourceCandidate>(), [], false, 1, TimeSpan.FromSeconds(1),
            _ => Task.FromResult(false), null, CancellationToken.None);
        Assert(second is null, "Parallele Suchläufe müssen verhindert werden.");
        gateProvider.Release.SetResult(true);
        Assert(await first is not null, "Erster Suchlauf wurde nicht abgeschlossen.");
    }
}
finally
{
    Directory.Delete(temp, true);
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("FEHLGESCHLAGEN:");
    foreach (var failure in failures) Console.Error.WriteLine("- " + failure);
    return 1;
}
Console.WriteLine("Alle ProxySammler-1.8.1-Tests erfolgreich.");
return 0;

sealed class PublicResolver : IAddressResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") });
}

sealed class FixedResolver(params IPAddress[] addresses) : IAddressResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) => Task.FromResult(addresses);
}

sealed class ReplyHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(reply(request));
}

sealed class BlockingProvider : ISourceDiscoveryProvider
{
    public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task<(IReadOnlyList<DiscoveredLink> Links, int Repositories)> SearchAsync(
        SafeSourceDownloader downloader, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        Entered.SetResult(true);
        await Release.Task;
        return ([], 0);
    }
}

sealed class EmptyProvider : ISourceDiscoveryProvider
{
    public Task<(IReadOnlyList<DiscoveredLink> Links, int Repositories)> SearchAsync(
        SafeSourceDownloader downloader, IProgress<string>? progress, CancellationToken cancellationToken) =>
        Task.FromResult<(IReadOnlyList<DiscoveredLink>, int)>(([], 0));
}
