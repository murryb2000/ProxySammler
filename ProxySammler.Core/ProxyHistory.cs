using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ProxySammler.Core;

public sealed class ProxyHistory
{
	private readonly Dictionary<string, ProxyHistoryEntry> _entries = new Dictionary<string, ProxyHistoryEntry>(StringComparer.OrdinalIgnoreCase);

	public static ProxyHistory Load(string path)
	{
		ProxyHistory proxyHistory = new ProxyHistory();
		if (!File.Exists(path))
		{
			return proxyHistory;
		}
		try
		{
			List<ProxyHistoryEntry> list = JsonSerializer.Deserialize<List<ProxyHistoryEntry>>(File.ReadAllText(path));
			if (list == null)
			{
				return proxyHistory;
			}
			foreach (ProxyHistoryEntry item in list)
			{
				proxyHistory._entries[Key(item.Protocol, item.Host, item.Port)] = item;
			}
		}
		catch
		{
		}
		return proxyHistory;
	}

	public IReadOnlyList<ProxyEndpoint> SelectNew(IEnumerable<ProxyEndpoint> proxies, out int alreadyKnown,
        bool skipRecentFailures = true, TimeSpan? failureCacheDuration = null)
	{
		List<ProxyEndpoint> list = new List<ProxyEndpoint>();
		alreadyKnown = 0;
		foreach (ProxyEndpoint proxy in proxies)
		{
			if (_entries.TryGetValue(Key(proxy), out var entry) &&
                (entry.IsWorking || (skipRecentFailures &&
                 DateTime.UtcNow - entry.CheckedAtUtc < (failureCacheDuration ?? TimeSpan.FromHours(6)))))
			{
				alreadyKnown++;
			}
			else
			{
				list.Add(proxy);
			}
		}
		return list;
	}

	public IReadOnlyList<ProxyEndpoint> Prepare(IEnumerable<ProxyEndpoint> proxies, bool skipRecentFailures, TimeSpan failureCacheDuration, out int skipped)
	{
		DateTime utcNow = DateTime.UtcNow;
		skipped = 0;
		List<(ProxyEndpoint Proxy, ProxyHistoryEntry? Entry)> list = new();
		foreach (ProxyEndpoint proxy in proxies)
		{
			_entries.TryGetValue(Key(proxy), out ProxyHistoryEntry value);
			if (skipRecentFailures && value != null && !value.IsWorking && utcNow - value.CheckedAtUtc < failureCacheDuration)
			{
				skipped++;
			}
			else
			{
				list.Add((proxy, value));
			}
		}
		return (from x in list
			orderby (!(x.Entry?.IsWorking ?? false)) ? 1 : 0, x.Entry?.ResponseTimeMs ?? long.MaxValue
			select x.Proxy).ToArray();
	}

	public void Update(IEnumerable<ProxyTestResult> results)
	{
		foreach (ProxyTestResult result in results)
		{
			_entries[Key(result.Proxy)] = new ProxyHistoryEntry
			{
				Protocol = result.Proxy.Protocol,
				Host = result.Proxy.Host,
				Port = result.Proxy.Port,
				IsWorking = result.IsWorking,
				ResponseTimeMs = result.ResponseTimeMs,
				CheckedAtUtc = result.CheckedAtUtc
			};
		}
	}

	public void Save(string path)
	{
		string directoryName = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		DateTime cutoff = DateTime.UtcNow.AddDays(-14.0);
		ProxyHistoryEntry[] value = (from x in _entries.Values
			where x.CheckedAtUtc >= cutoff
			orderby x.CheckedAtUtc descending
			select x).ToArray();
		File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions
		{
			WriteIndented = true
		}));
	}

	private static string Key(ProxyEndpoint proxy)
	{
		return Key(proxy.Protocol, proxy.Host, proxy.Port);
	}

	private static string Key(ProxyProtocol protocol, string host, int port)
	{
		return $"{protocol}|{host}|{port}";
	}
}
