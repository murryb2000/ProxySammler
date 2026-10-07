using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

namespace ProxySammler.Core;

public interface IAddressResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class SystemAddressResolver : IAddressResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}

public static class PublicUrlPolicy
{
    public static async Task<IPAddress[]> ValidateAsync(Uri uri, IAddressResolver resolver, CancellationToken cancellationToken)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Nur HTTP- und HTTPS-Adressen sind erlaubt.");
        if (uri.UserInfo.Length > 0 || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Lokale Adressen sind für gefundene Quellen gesperrt.");
        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host, out var literal)) addresses = [literal];
        else addresses = await resolver.ResolveAsync(uri.IdnHost, cancellationToken);
        var ipv4 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork || a.IsIPv4MappedToIPv6)
            .Select(a => a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a).ToArray();
        if (ipv4.Length == 0 || ipv4.Any(a => !IsPublic(a)))
            throw new InvalidDataException("Die Quelle verweist auf eine private oder reservierte Netzwerkadresse.");
        return ipv4;
    }

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetwork) return false; // IPv6 wird bewusst nicht abgerufen.
        var b = address.GetAddressBytes();
        return b[0] is > 0 and < 224 &&
            b[0] != 10 && b[0] != 127 && b[0] != 169 &&
            !(b[0] == 100 && b[1] is >= 64 and <= 127) &&
            !(b[0] == 172 && b[1] is >= 16 and <= 31) &&
            !(b[0] == 192 && (b[1] == 0 || b[1] == 168 || b[1] == 88 && b[2] == 99)) &&
            !(b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51 && b[2] == 100)) &&
            !(b[0] == 203 && b[1] == 0 && b[2] == 113);
    }
}

public sealed record SourceDownload(string Text, Uri FinalUri, HttpResponseHeaders Headers, DateTimeOffset? LastModified);

public sealed class RateLimitException : Exception
{
    public DateTimeOffset? RetryAt { get; }
    public RateLimitException(DateTimeOffset? retryAt) : base(retryAt is null
        ? "GitHub begrenzt die Suche. Bitte später erneut versuchen."
        : $"GitHub begrenzt die Suche. Nächster Versuch ab {retryAt.Value.ToLocalTime():g}.") => RetryAt = retryAt;
}

public sealed class SafeSourceDownloader : IDisposable
{
    private readonly IAddressResolver _resolver;
    private readonly HttpClient _client;

    public SafeSourceDownloader(IAddressResolver? resolver = null, HttpMessageHandler? testHandler = null)
    {
        _resolver = resolver ?? new SystemAddressResolver();
        var handler = testHandler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            ConnectCallback = async (context, ct) =>
            {
                var host = context.DnsEndPoint.Host;
                var uri = new UriBuilder("https", host, context.DnsEndPoint.Port).Uri;
                var addresses = await PublicUrlPolicy.ValidateAsync(uri, _resolver, ct);
                var socket = new Socket(addresses[0].AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(addresses[0], context.DnsEndPoint.Port, ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            }
        };
        _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("ProxySammler/1.8.0");
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<SourceDownload> GetAsync(string url, CancellationToken cancellationToken)
    {
        var current = new Uri(url, UriKind.Absolute);
        for (var redirect = 0; redirect <= DiscoveryPolicy.MaxRedirects; redirect++)
        {
            await PublicUrlPolicy.ValidateAsync(current, _resolver, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (current.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                if (redirect == DiscoveryPolicy.MaxRedirects || response.Headers.Location is null)
                    throw new InvalidDataException("Zu viele Weiterleitungen der Quelle.");
                current = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(current, response.Headers.Location);
                continue;
            }
            if ((int)response.StatusCode is 403 or 429 && current.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
            {
                DateTimeOffset? retry = null;
                if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values) &&
                    long.TryParse(values.FirstOrDefault(), out var seconds)) retry = DateTimeOffset.FromUnixTimeSeconds(seconds);
                else if (response.Headers.RetryAfter?.Delta is { } delay) retry = DateTimeOffset.UtcNow.Add(delay);
                throw new RateLimitException(retry);
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > DiscoveryPolicy.MaxDownloadBytes)
                throw new InvalidDataException("Quelle ist größer als 8 MB.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var memory = new MemoryStream();
            var buffer = new byte[16_384];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (memory.Length + read > DiscoveryPolicy.MaxDownloadBytes)
                    throw new InvalidDataException("Quelle ist größer als 8 MB.");
                memory.Write(buffer, 0, read);
            }
            return new SourceDownload(Encoding.UTF8.GetString(memory.ToArray()), current, response.Headers,
                response.Content.Headers.LastModified);
        }
        throw new InvalidDataException("Zu viele Weiterleitungen der Quelle.");
    }

    public void Dispose() => _client.Dispose();
}
