using System;

namespace ProxySammler.Core;

public sealed class ProxyTestResult
{
	public required ProxyEndpoint Proxy { get; init; }

	public bool IsWorking { get; init; }

	public long ResponseTimeMs { get; init; }

	public string ExitIp { get; init; } = "";

	public string Error { get; init; } = "";

	public DateTime CheckedAtUtc { get; init; } = DateTime.UtcNow;
}
