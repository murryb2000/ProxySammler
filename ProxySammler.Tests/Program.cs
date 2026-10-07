using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProxySammler.Core;
using ProxySammler.Persistence;

var failures = new List<string>();
void Assert(bool condition, string message) { if (!condition) failures.Add(message); }

var temp = Path.Combine(Path.GetTempPath(), "ProxySammler-1.6-Test-" + Guid.NewGuid().ToString("N"));
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
Console.WriteLine("Alle ProxySammler-1.6-Tests erfolgreich.");
return 0;
