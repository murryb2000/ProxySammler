namespace ProxySammler.Core;

public sealed record ProxyEndpoint(ProxyProtocol Protocol, string Host, int Port)
{
	public string Address => $"{Host}:{Port}";

	public string Url => ProtocolName.ToLowerInvariant() + "://" + Address;

	public string ProtocolName => Protocol switch
	{
		ProxyProtocol.Http => "HTTP", 
		ProxyProtocol.Socks4 => "SOCKS4", 
		_ => "SOCKS5", 
	};
}
