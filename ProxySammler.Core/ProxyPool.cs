using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ProxySammler.Core;

public sealed class ProxyPool
{
	private readonly Dictionary<string, ProxyEndpoint> _proxies = new Dictionary<string, ProxyEndpoint>(StringComparer.OrdinalIgnoreCase);

	public int Count => _proxies.Count;

	public IReadOnlyCollection<ProxyEndpoint> Proxies => (IReadOnlyCollection<ProxyEndpoint>)(object)_proxies.Values.ToArray();

	public static ProxyPool Load(string directory)
	{
		ProxyPool proxyPool = new ProxyPool();
		string path = Path.Combine(directory, "gefundene_all.txt");
		if (!File.Exists(path))
		{
			return proxyPool;
		}
		try
		{
			foreach (ProxyEndpoint item in ProxyCollector.Extract(File.ReadAllText(path), ProxyProtocol.Http))
			{
				proxyPool._proxies[Key(item)] = item;
			}
		}
		catch
		{
		}
		return proxyPool;
	}

	public int AddWorking(IEnumerable<ProxyTestResult> results)
	{
		int num = 0;
		foreach (ProxyTestResult item in results.Where((ProxyTestResult r) => r.IsWorking))
		{
			if (_proxies.TryAdd(Key(item.Proxy), item.Proxy))
			{
				num++;
			}
		}
		return num;
	}

	public IReadOnlyList<ProxyEndpoint> Select(ISet<ProxyProtocol> protocols)
	{
		return (from p in _proxies.Values
			where protocols.Contains(p.Protocol)
			orderby p.Protocol
			select p).ThenBy<ProxyEndpoint, string>((ProxyEndpoint p) => p.Host, StringComparer.Ordinal).ThenBy((ProxyEndpoint p) => p.Port).ToArray();
	}

	public int ReplaceSelected(ISet<ProxyProtocol> protocols, IEnumerable<ProxyTestResult> results)
	{
		int num = _proxies.Values.Count((ProxyEndpoint p) => protocols.Contains(p.Protocol));
		string[] array = (from x in _proxies
			where protocols.Contains(x.Value.Protocol)
			select x.Key).ToArray();
		foreach (string key in array)
		{
			_proxies.Remove(key);
		}
		AddWorking(results);
		int num3 = _proxies.Values.Count((ProxyEndpoint p) => protocols.Contains(p.Protocol));
		return Math.Max(0, num - num3);
	}

	public void Save(string directory, bool createBackup)
	{
		Directory.CreateDirectory(directory);
		string text = Path.Combine(directory, "gefundene_all.txt");
		if (createBackup && File.Exists(text))
		{
			string text2 = Path.Combine(directory, "Archiv");
			Directory.CreateDirectory(text2);
			string destFileName = Path.Combine(text2, $"gefundene_all_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.txt");
			File.Copy(text, destFileName, overwrite: false);
		}
		ProxyEndpoint[] array = _proxies.Values.OrderBy((ProxyEndpoint p) => p.Protocol).ThenBy<ProxyEndpoint, string>((ProxyEndpoint p) => p.Host, StringComparer.Ordinal).ThenBy((ProxyEndpoint p) => p.Port)
			.ToArray();
		Write(Path.Combine(directory, "gefundene_all.txt"), array.Select((ProxyEndpoint p) => p.Url));
		Write(Path.Combine(directory, "gefundene_ip-port.txt"), array.Select((ProxyEndpoint p) => p.Address).Distinct<string>(StringComparer.OrdinalIgnoreCase));
		Write(Path.Combine(directory, "gefundene_http.txt"), Addresses(array, ProxyProtocol.Http));
		Write(Path.Combine(directory, "gefundene_socks4.txt"), Addresses(array, ProxyProtocol.Socks4));
		Write(Path.Combine(directory, "gefundene_socks5.txt"), Addresses(array, ProxyProtocol.Socks5));
	}

	private static IEnumerable<string> Addresses(IEnumerable<ProxyEndpoint> proxies, ProxyProtocol protocol)
	{
		return from p in proxies
			where p.Protocol == protocol
			select p.Address;
	}

	private static void Write(string path, IEnumerable<string> lines)
	{
		File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
	}

	private static string Key(ProxyEndpoint proxy)
	{
		return $"{proxy.Protocol}|{proxy.Host}|{proxy.Port}";
	}
}
