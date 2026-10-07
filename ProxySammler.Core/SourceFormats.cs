using System.Text.RegularExpressions;
using System.Text.Json;
using System.Net;

namespace ProxySammler.Core;

public static class SourceFormats
{
    public static ProxyProtocol InferProtocol(string url) =>
        url.Contains("socks5", StringComparison.OrdinalIgnoreCase) ? ProxyProtocol.Socks5 :
        url.Contains("socks4", StringComparison.OrdinalIgnoreCase) ? ProxyProtocol.Socks4 : ProxyProtocol.Http;

    public static IReadOnlyList<ProxyEndpoint> Extract(string content, ProxyProtocol fallback)
    {
        var found = ProxyCollector.Extract(content, fallback).ToList();
        if (content.TrimStart().StartsWith('{') || content.TrimStart().StartsWith('['))
        {
            try
            {
                using var document = JsonDocument.Parse(content);
                ReadJson(document.RootElement, fallback, found);
            }
            catch (JsonException) { /* Auch textförmige Antworten mit JSON-Fragmenten bleiben nutzbar. */ }
        }
        else ReadCsv(content, fallback, found);
        return found.Distinct().Take(100_000).ToArray();
    }

    private static void ReadJson(JsonElement element, ProxyProtocol fallback, List<ProxyEndpoint> found)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) ReadJson(item, fallback, found);
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            string? Value(params string[] names)
            {
                foreach (var property in element.EnumerateObject())
                    if (names.Contains(property.Name, StringComparer.OrdinalIgnoreCase)) return property.Value.ToString();
                return null;
            }
            Add(Value("ip", "host", "ipAddress"), Value("port"), Value("protocol", "type", "scheme"), fallback, found);
            foreach (var property in element.EnumerateObject())
                if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                    ReadJson(property.Value, fallback, found);
        }
    }

    private static void ReadCsv(string content, ProxyProtocol fallback, List<ProxyEndpoint> found)
    {
        var lines = content.Split('\n');
        var ipIndex = 0;
        var portIndex = 1;
        var protocolIndex = 2;
        foreach (var line in lines.Take(100_000))
        {
            var separator = line.Contains(',') ? ',' : line.Contains(';') ? ';' : '\t';
            var fields = line.Trim().Split(separator).Select(x => x.Trim().Trim('"')).ToArray();
            if (fields.Length < 2) continue;
            if (fields.Any(x => x.Equals("ip", StringComparison.OrdinalIgnoreCase) || x.Equals("port", StringComparison.OrdinalIgnoreCase)))
            {
                ipIndex = Array.FindIndex(fields, x => x.Equals("ip", StringComparison.OrdinalIgnoreCase) || x.Equals("host", StringComparison.OrdinalIgnoreCase));
                portIndex = Array.FindIndex(fields, x => x.Equals("port", StringComparison.OrdinalIgnoreCase));
                protocolIndex = Array.FindIndex(fields, x => x.Equals("protocol", StringComparison.OrdinalIgnoreCase) || x.Equals("type", StringComparison.OrdinalIgnoreCase));
                continue;
            }
            if (ipIndex >= 0 && portIndex >= 0 && ipIndex < fields.Length && portIndex < fields.Length)
                Add(fields[ipIndex], fields[portIndex], protocolIndex >= 0 && protocolIndex < fields.Length ? fields[protocolIndex] : null, fallback, found);
        }
    }

    private static void Add(string? host, string? portText, string? type, ProxyProtocol fallback, List<ProxyEndpoint> found)
    {
        if (!IPAddress.TryParse(host, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            !int.TryParse(portText, out var port) || port is < 1 or > 65535) return;
        var protocol = type?.Trim().ToLowerInvariant() switch
        {
            "socks4" or "socks4a" => ProxyProtocol.Socks4,
            "socks5" or "socks5h" => ProxyProtocol.Socks5,
            "http" or "https" => ProxyProtocol.Http,
            _ => fallback
        };
        found.Add(new ProxyEndpoint(protocol, ip.ToString(), port));
    }

    public static string NormalizeUrl(string url)
    {
        var uri = new Uri(url.Trim(), UriKind.Absolute);
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Nur HTTP- und HTTPS-Adressen sind erlaubt.");
        if (uri.UserInfo.Length > 0) throw new ArgumentException("Adressen mit Zugangsdaten sind nicht erlaubt.");
        return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped).TrimEnd('/');
    }

    public static IReadOnlyList<DiscoveredLink> ExtractReadmeLinks(string readme, string provider, DateTimeOffset? updated)
    {
        var links = new List<DiscoveredLink>();
        foreach (Match match in Regex.Matches(readme, @"https?://[^\s<>\)\]""']+", RegexOptions.IgnoreCase))
        {
            var url = match.Value.TrimEnd('.', ',', ';');
            if (!LooksLikeList(url)) continue;
            try { links.Add(new DiscoveredLink(NormalizeUrl(url), provider, InferProtocol(url), updated)); }
            catch (ArgumentException) { }
            catch (UriFormatException) { }
        }
        return links.DistinctBy(x => x.Url, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool LooksLikeList(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var path = uri.AbsolutePath.ToLowerInvariant();
        return (path.EndsWith(".txt") || path.EndsWith(".csv") || path.EndsWith(".json") ||
                path.Contains("/api/") || uri.Host.StartsWith("api.", StringComparison.OrdinalIgnoreCase)) &&
               (url.Contains("proxy", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("socks", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith("http.txt"));
    }
}
