using System;

namespace ProxySammler.Core;

public sealed class ProxyHistoryEntry
{
	public ProxyProtocol Protocol { get; set; }

	public string Host { get; set; } = "";

	public int Port { get; set; }

	public bool IsWorking { get; set; }

	public int FailureStreak { get; set; }

	public long ResponseTimeMs { get; set; }

	public DateTime CheckedAtUtc { get; set; }
}
