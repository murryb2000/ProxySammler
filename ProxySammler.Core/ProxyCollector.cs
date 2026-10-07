using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ProxySammler.Core;

public sealed class ProxyCollector
{
	private static readonly Regex ProxyPattern = new(
		"(?:(?<scheme>https?|socks4a?|socks5h?)://)?(?<host>(?:\\d{1,3}\\.){3}\\d{1,3}):(?<port>\\d{1,5})",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

	private const int MaxSourceBytes = 8388608;

	private const int MaxProxiesPerSource = 100000;

	private readonly HttpClient _http;

	public ProxyCollector(HttpClient? httpClient = null)
	{
		_http = httpClient ?? new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All
		});
		_http.Timeout = TimeSpan.FromSeconds(15.0);
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("ProxySammler/1.8.0");
	}

	public static IReadOnlyList<SourceDefinition> ReadSources(string path)
	{
		if (!File.Exists(path))
		{
			throw new FileNotFoundException("Die Datei sources.txt wurde nicht gefunden.", path);
		}
		List<SourceDefinition> list = new List<SourceDefinition>();
		foreach (string item in File.ReadLines(path))
		{
			string text = item.Trim();
			if (text.Length != 0 && !text.StartsWith('#'))
			{
				string[] array = text.Split('|', 2, StringSplitOptions.TrimEntries);
				if (array.Length == 2 && Uri.TryCreate(array[1], UriKind.Absolute, out Uri result) && (!(result.Scheme != Uri.UriSchemeHttp) || !(result.Scheme != Uri.UriSchemeHttps)) && TryProtocol(array[0], out var protocol))
				{
					list.Add(new SourceDefinition(protocol, result.AbsoluteUri));
				}
			}
		}
		return list;
	}

	public async Task<CollectionResult> CollectAsync(IEnumerable<SourceDefinition> sources, ISet<ProxyProtocol> enabledProtocols, IProgress<string>? progress, CancellationToken cancellationToken)
	{
		SourceDefinition[] selected = sources.Where((SourceDefinition s) => enabledProtocols.Contains(s.Protocol)).ToArray();
		ConcurrentBag<SourceResult> sourceResults = new ConcurrentBag<SourceResult>();
		ConcurrentDictionary<string, ProxyEndpoint> unique = new ConcurrentDictionary<string, ProxyEndpoint>(StringComparer.OrdinalIgnoreCase);
		await Parallel.ForEachAsync(selected, new ParallelOptions
		{
			MaxDegreeOfParallelism = Math.Min(8, Math.Max(1, selected.Length)),
			CancellationToken = cancellationToken
		}, async delegate(SourceDefinition source, CancellationToken ct)
		{
			progress?.Report($"Lade {source.Protocol}: {new Uri(source.Url).Host}");
			try
			{
				using HttpResponseMessage response = await _http.GetAsync(source.Url, HttpCompletionOption.ResponseHeadersRead, ct);
				response.EnsureSuccessStatusCode();
				if (response.Content.Headers.ContentLength > 8388608)
				{
					throw new InvalidDataException("Quelle ist größer als 8 MB.");
				}
				await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
				using StreamReader reader = new StreamReader(stream);
				char[] buffer = new char[8192];
				StringBuilder text = new StringBuilder();
				while (true)
				{
					int num = await reader.ReadAsync(buffer.AsMemory(), ct);
					if (num == 0)
					{
						break;
					}
					text.Append(buffer, 0, num);
					if (text.Length > 8388608)
					{
						throw new InvalidDataException("Quelle ist größer als 8 MB.");
					}
				}
				ProxyEndpoint[] array = Extract(text.ToString(), source.Protocol).Take(100000).ToArray();
				ProxyEndpoint[] array2 = array;
				foreach (ProxyEndpoint proxyEndpoint in array2)
				{
					unique[$"{proxyEndpoint.Protocol}|{proxyEndpoint.Host}|{proxyEndpoint.Port}"] = proxyEndpoint;
				}
				sourceResults.Add(new SourceResult(source, array.Length, null));
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				sourceResults.Add(new SourceResult(source, 0, ShortError(ex)));
			}
		});
		progress?.Report($"{unique.Count:N0} eindeutige Proxys gesammelt.");
		return new CollectionResult(unique.Values.ToArray(), sourceResults.OrderBy((SourceResult x) => Array.IndexOf(selected, x.Source)).ToArray());
	}

	public static IEnumerable<ProxyEndpoint> Extract(string text, ProxyProtocol fallback)
	{
		int result = default(int);
		foreach (Match item in ProxyRegex().Matches(text))
		{
			IPAddress address;
			bool flag = !IPAddress.TryParse(item.Groups["host"].Value, out address) || address.AddressFamily != AddressFamily.InterNetwork || !int.TryParse(item.Groups["port"].Value, out result);
			if (!flag)
			{
				bool flag2 = ((result < 1 || result > 65535) ? true : false);
				flag = flag2;
			}
			if (!flag)
			{
				ProxyProtocol protocol = fallback;
				string value = item.Groups["scheme"].Value;
				if (value.Length <= 0 || TryProtocol(value, out protocol))
				{
					yield return new ProxyEndpoint(protocol, address.ToString(), result);
				}
			}
		}
	}

	private static bool TryProtocol(string value, out ProxyProtocol protocol)
	{
		switch (value.Trim().ToLowerInvariant())
		{
		case "http":
		case "https":
			protocol = ProxyProtocol.Http;
			return true;
		case "socks4":
		case "socks4a":
			protocol = ProxyProtocol.Socks4;
			return true;
		case "socks5":
		case "socks5h":
			protocol = ProxyProtocol.Socks5;
			return true;
		default:
			protocol = ProxyProtocol.Http;
			return false;
		}
	}

	private static string ShortError(Exception ex)
	{
		string text = ex.GetBaseException().Message.Replace('\r', ' ').Replace('\n', ' ');
		if (text.Length > 140)
		{
			return text.Substring(0, 140) + "…";
		}
		return text;
	}

	private static Regex ProxyRegex()
	{
		return ProxyPattern;
	}
}
