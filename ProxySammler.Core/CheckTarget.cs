namespace ProxySammler.Core;

public sealed class CheckTarget
{
	public string Host { get; init; } = "ipv4.icanhazip.com";

	public int Port { get; init; } = 443;

	public string Path { get; init; } = "/";

	public bool UseTls { get; init; } = true;
}
