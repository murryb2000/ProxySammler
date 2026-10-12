using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ProxySammler.Core;

namespace ProxySammler.Persistence;

public sealed class ProxyListStore
{
    private readonly Dictionary<string, ProxyEndpoint> _proxies = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _proxies.Count;

    public static ProxyListStore Load(string resultsDirectory, string? legacyPoolDirectory = null)
    {
        var store = new ProxyListStore();
        foreach (var protocol in Enum.GetValues<ProxyProtocol>())
        {
            var currentPath = Path.Combine(resultsDirectory, FileName(protocol));
            if (File.Exists(currentPath))
            {
                store.LoadFile(currentPath, protocol);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(legacyPoolDirectory))
            {
                var legacyPath = Path.Combine(legacyPoolDirectory, LegacyFileName(protocol));
                if (File.Exists(legacyPath))
                {
                    store.LoadFile(legacyPath, protocol);
                    continue;
                }
            }

            var previousCurrent = Path.Combine(resultsDirectory, PreviousCurrentFileName(protocol));
            if (File.Exists(previousCurrent)) store.LoadFile(previousCurrent, protocol);
        }
        return store;
    }

    public int AddWorking(IEnumerable<ProxyTestResult> results)
    {
        var added = 0;
        foreach (var result in results.Where(r => r.IsWorking))
            if (_proxies.TryAdd(Key(result.Proxy), result.Proxy)) added++;
        return added;
    }

    public IReadOnlyList<ProxyEndpoint> Select(ISet<ProxyProtocol> protocols) => _proxies.Values
        .Where(p => protocols.Contains(p.Protocol))
        .OrderBy(p => p.Protocol)
        .ThenBy(p => p.Host, StringComparer.Ordinal)
        .ThenBy(p => p.Port)
        .ToArray();

    public int ReplaceSelected(ISet<ProxyProtocol> protocols, IEnumerable<ProxyTestResult> results)
    {
        var oldCount = _proxies.Values.Count(p => protocols.Contains(p.Protocol));
        foreach (var key in _proxies.Where(x => protocols.Contains(x.Value.Protocol)).Select(x => x.Key).ToArray())
            _proxies.Remove(key);
        AddWorking(results);
        var newCount = _proxies.Values.Count(p => protocols.Contains(p.Protocol));
        return Math.Max(0, oldCount - newCount);
    }

    public void Save(string resultsDirectory)
    {
        Directory.CreateDirectory(resultsDirectory);
        foreach (var protocol in Enum.GetValues<ProxyProtocol>())
        {
            var lines = _proxies.Values.Where(p => p.Protocol == protocol)
                .OrderBy(p => p.Host, StringComparer.Ordinal)
                .ThenBy(p => p.Port)
                .Select(p => p.Address);
            var values = lines.ToArray();
            AtomicTextFile.Write(Path.Combine(resultsDirectory, FileName(protocol)),
                values.Length == 0 ? "" : string.Join(Environment.NewLine, values) + Environment.NewLine,
                createBackup: false);
        }
    }

    private void LoadFile(string path, ProxyProtocol protocol)
    {
        try
        {
            foreach (var proxy in ProxyCollector.Extract(File.ReadAllText(path), protocol))
            {
                var normalized = proxy with { Protocol = protocol };
                _proxies[Key(normalized)] = normalized;
            }
        }
        catch
        {
            // Eine beschädigte alte Liste darf den Programmstart nicht verhindern.
        }
    }

    private static string FileName(ProxyProtocol protocol) => protocol switch
    {
        ProxyProtocol.Http => "HTTP.txt",
        ProxyProtocol.Socks4 => "SOCKS4.txt",
        _ => "SOCKS5.txt"
    };

    private static string LegacyFileName(ProxyProtocol protocol) => protocol switch
    {
        ProxyProtocol.Http => "gefundene_http.txt",
        ProxyProtocol.Socks4 => "gefundene_socks4.txt",
        _ => "gefundene_socks5.txt"
    };

    private static string PreviousCurrentFileName(ProxyProtocol protocol) => protocol switch
    {
        ProxyProtocol.Http => "aktuell_http.txt",
        ProxyProtocol.Socks4 => "aktuell_socks4.txt",
        _ => "aktuell_socks5.txt"
    };

    private static string Key(ProxyEndpoint proxy) => $"{proxy.Protocol}|{proxy.Host}|{proxy.Port}";
}
