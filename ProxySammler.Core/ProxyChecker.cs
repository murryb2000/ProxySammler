using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ProxySammler.Core;

public sealed class ProxyChecker
{
	private readonly CheckTarget _target;

	private readonly ProxyCheckProfile _profile;

	public ProxyChecker(CheckTarget? target = null, ProxyCheckProfile profile = ProxyCheckProfile.SecureGeneric)
	{
		_target = target ?? new CheckTarget();
		_profile = profile;
	}

	public async Task<IReadOnlyList<ProxyTestResult>> CheckAllAsync(IReadOnlyCollection<ProxyEndpoint> proxies, int concurrency, TimeSpan timeout, IProgress<ProxyTestResult>? resultProgress, IProgress<(int done, int total)>? countProgress, CancellationToken cancellationToken)
	{
		ConcurrentBag<ProxyTestResult> results = new ConcurrentBag<ProxyTestResult>();
		int done = 0;
		await Parallel.ForEachAsync(proxies, new ParallelOptions
		{
			MaxDegreeOfParallelism = Math.Clamp(concurrency, 1, 100),
			CancellationToken = cancellationToken
		}, async delegate(ProxyEndpoint proxy, CancellationToken ct)
		{
			ProxyTestResult proxyTestResult = await CheckOneAsync(proxy, timeout, ct);
			results.Add(proxyTestResult);
			resultProgress?.Report(proxyTestResult);
			countProgress?.Report((Interlocked.Increment(ref done), proxies.Count));
		});
		return (from r in results
			orderby r.IsWorking descending, (!r.IsWorking) ? long.MaxValue : r.ResponseTimeMs, r.Proxy.Protocol
			select r).ThenBy<ProxyTestResult, string>((ProxyTestResult r) => r.Proxy.Host, StringComparer.Ordinal).ThenBy((ProxyTestResult r) => r.Proxy.Port).ToArray();
	}

	public async Task<IReadOnlyList<ProxyTestResult>> CheckAllTurboAsync(IReadOnlyCollection<ProxyEndpoint> proxies, int botCount, TimeSpan timeout, TimeSpan precheckTimeout, IProgress<ProxyTestResult>? resultProgress, IProgress<CheckProgress>? phaseProgress, CancellationToken cancellationToken)
	{
		ConcurrentBag<ProxyEndpoint> survivors = new ConcurrentBag<ProxyEndpoint>();
		ConcurrentBag<ProxyTestResult> results = new ConcurrentBag<ProxyTestResult>();
		int prechecked = 0;
		phaseProgress?.Report(new CheckProgress("Schnelltest", 0, proxies.Count));
		await Parallel.ForEachAsync(proxies, CreateParallelOptions(botCount, cancellationToken), async delegate(ProxyEndpoint proxy, CancellationToken ct)
		{
			ProxyTestResult proxyTestResult = await FastPortCheckAsync(proxy, precheckTimeout, ct);
			if (proxyTestResult == null)
			{
				survivors.Add(proxy);
			}
			else
			{
				results.Add(proxyTestResult);
				resultProgress?.Report(proxyTestResult);
			}
			phaseProgress?.Report(new CheckProgress("Schnelltest", Interlocked.Increment(ref prechecked), proxies.Count));
		});
		ProxyEndpoint[] candidates = survivors.ToArray();
		int verified = 0;
		phaseProgress?.Report(new CheckProgress("HTTPS-Prüfung", 0, candidates.Length));
		await Parallel.ForEachAsync(candidates, CreateParallelOptions(botCount, cancellationToken), async delegate(ProxyEndpoint proxy, CancellationToken ct)
		{
			ProxyTestResult proxyTestResult = await CheckOneAsync(proxy, timeout, ct);
			results.Add(proxyTestResult);
			resultProgress?.Report(proxyTestResult);
			phaseProgress?.Report(new CheckProgress("HTTPS-Prüfung", Interlocked.Increment(ref verified), candidates.Length));
		});
		return Sort(results);
	}

	public async Task<ProxyTestResult> CheckOneAsync(ProxyEndpoint proxy, TimeSpan timeout, CancellationToken cancellationToken)
	{
		if (_profile == ProxyCheckProfile.MacAttackCompatible)
		{
			return await CheckMacAttackCompatibleAsync(proxy, timeout, cancellationToken);
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(timeout);
		try
		{
			using TcpClient client = new TcpClient(AddressFamily.InterNetwork);
			await client.ConnectAsync(proxy.Host, proxy.Port, timeoutCts.Token);
			client.NoDelay = true;
			NetworkStream stream = client.GetStream();
			switch (proxy.Protocol)
			{
			case ProxyProtocol.Http:
				await EstablishHttpTunnel(stream, timeoutCts.Token);
				break;
			case ProxyProtocol.Socks4:
				await EstablishSocks4aTunnel(stream, timeoutCts.Token);
				break;
			case ProxyProtocol.Socks5:
				await EstablishSocks5Tunnel(stream, timeoutCts.Token);
				break;
			}
			Stream requestStream = stream;
			if (_target.UseTls)
			{
				SslStream tls = new SslStream(stream, leaveInnerStreamOpen: false);
				await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
				{
					TargetHost = _target.Host,
					EnabledSslProtocols = (SslProtocols.Tls12 | SslProtocols.Tls13),
					CertificateRevocationCheckMode = X509RevocationMode.NoCheck
				}, timeoutCts.Token);
				requestStream = tls;
			}
			byte[] bytes = Encoding.ASCII.GetBytes($"GET {_target.Path} HTTP/1.1\r\nHost: {_target.Host}\r\n" + "User-Agent: ProxySammler/1.5\r\nAccept: text/plain\r\nConnection: close\r\n\r\n");
			await requestStream.WriteAsync(bytes, timeoutCts.Token);
			await requestStream.FlushAsync(timeoutCts.Token);
			string exitIp = ParseExitIp(await ReadResponse(requestStream, timeoutCts.Token));
			stopwatch.Stop();
			return new ProxyTestResult
			{
				Proxy = proxy,
				IsWorking = true,
				ResponseTimeMs = stopwatch.ElapsedMilliseconds,
				ExitIp = exitIp
			};
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return Failed(proxy, stopwatch, "Zeitüberschreitung");
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex3)
		{
			return Failed(proxy, stopwatch, FriendlyError(ex3));
		}
	}

	private static async Task<ProxyTestResult> CheckMacAttackCompatibleAsync(ProxyEndpoint proxy, TimeSpan timeout, CancellationToken cancellationToken)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		if (proxy.Protocol != ProxyProtocol.Http)
		{
			return Failed(proxy, stopwatch, "MacAttack unterstützt in seiner Proxyprüfung nur HTTP-Proxys.");
		}
		using (CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
		{
			timeoutCts.CancelAfter(timeout);
			try
			{
				using (HttpClientHandler handler = new HttpClientHandler
				{
					Proxy = new WebProxy("http://" + proxy.Address),
					UseProxy = true,
					AutomaticDecompression = DecompressionMethods.All,
					AllowAutoRedirect = true,
					MaxAutomaticRedirections = 10
				})
				{
					using HttpClient client = new HttpClient(handler)
					{
						Timeout = Timeout.InfiniteTimeSpan
					};
					using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "http://httpbin.org/anything?user=Evilvirus&application=MacAttack");
					request.Headers.UserAgent.ParseAdd("ProxySammler/1.5-MacAttack-Test");
					using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCts.Token);
					if (response.StatusCode != HttpStatusCode.OK)
					{
						throw new IOException($"Testziel meldet HTTP {response.StatusCode}.");
					}
					ProxyTestResult result;
					await using (Stream body = await response.Content.ReadAsStreamAsync(timeoutCts.Token))
					{
						CancellationToken token = timeoutCts.Token;
						using JsonDocument jsonDocument = await JsonDocument.ParseAsync(body, default(JsonDocumentOptions), token);
						JsonElement rootElement = jsonDocument.RootElement;
						JsonElement value;
						JsonElement value2;
						string? a = ((rootElement.TryGetProperty("args", out value) && value.TryGetProperty("user", out value2)) ? value2.GetString() : null);
						JsonElement value3;
						string text = (rootElement.TryGetProperty("origin", out value3) ? value3.GetString() : null);
						if (!string.Equals(a, "Evilvirus", StringComparison.Ordinal))
						{
							throw new IOException("Das Testziel hat die Kontrollkennung nicht zurückgegeben.");
						}
						if (!string.Equals(text, proxy.Host, StringComparison.OrdinalIgnoreCase))
						{
							throw new IOException($"Ausgangs-IP {text ?? "unbekannt"} entspricht nicht der Proxy-IP {proxy.Host}.");
						}
						stopwatch.Stop();
						result = new ProxyTestResult
						{
							Proxy = proxy,
							IsWorking = true,
							ResponseTimeMs = stopwatch.ElapsedMilliseconds,
							ExitIp = text
						};
					}
					return result;
				}
				IL_056a:;
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				return Failed(proxy, stopwatch, "Zeitüberschreitung");
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex3)
			{
				return Failed(proxy, stopwatch, FriendlyError(ex3));
			}
		}
		ProxyTestResult result2;
		return result2;
	}

	private static async Task<ProxyTestResult?> FastPortCheckAsync(ProxyEndpoint proxy, TimeSpan timeout, CancellationToken cancellationToken)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(timeout);
		try
		{
			using TcpClient client = new TcpClient(AddressFamily.InterNetwork);
			await client.ConnectAsync(proxy.Host, proxy.Port, timeoutCts.Token);
			return null;
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return Failed(proxy, stopwatch, "Schnelltest: Zeitüberschreitung");
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			return Failed(proxy, stopwatch, "Schnelltest: nicht erreichbar");
		}
	}

	private static ParallelOptions CreateParallelOptions(int botCount, CancellationToken cancellationToken)
	{
		return new ParallelOptions
		{
			MaxDegreeOfParallelism = Math.Clamp(botCount, 1, 100),
			CancellationToken = cancellationToken
		};
	}

	private static IReadOnlyList<ProxyTestResult> Sort(IEnumerable<ProxyTestResult> results)
	{
		return (from r in results
			orderby r.IsWorking descending, (!r.IsWorking) ? long.MaxValue : r.ResponseTimeMs, r.Proxy.Protocol
			select r).ThenBy<ProxyTestResult, string>((ProxyTestResult r) => r.Proxy.Host, StringComparer.Ordinal).ThenBy((ProxyTestResult r) => r.Proxy.Port).ToArray();
	}

	private async Task EstablishHttpTunnel(Stream stream, CancellationToken ct)
	{
		byte[] bytes = Encoding.ASCII.GetBytes($"CONNECT {_target.Host}:{_target.Port} HTTP/1.1\r\nHost: {_target.Host}:{_target.Port}\r\n" + "User-Agent: ProxySammler/1.5\r\nProxy-Connection: keep-alive\r\n\r\n");
		await stream.WriteAsync(bytes, ct);
		string text = (await ReadHeader(stream, ct)).Split("\r\n", 2)[0];
		if (!text.Contains(" 200 ", StringComparison.Ordinal))
		{
			throw new IOException("CONNECT abgelehnt (" + text + ")");
		}
	}

	private async Task EstablishSocks4aTunnel(Stream stream, CancellationToken ct)
	{
		byte[] bytes = Encoding.ASCII.GetBytes(_target.Host);
		using MemoryStream data = new MemoryStream();
		data.WriteByte(4);
		data.WriteByte(1);
		data.WriteByte((byte)(_target.Port >> 8));
		data.WriteByte((byte)(_target.Port & 0xFF));
		data.Write(new byte[4] { 0, 0, 0, 1 });
		data.WriteByte(0);
		data.Write(bytes);
		data.WriteByte(0);
		await stream.WriteAsync(data.ToArray(), ct);
		byte[] array = await ReadExact(stream, 8, ct);
		if (array[1] != 90)
		{
			throw new IOException($"SOCKS4-Verbindung abgelehnt (Code {array[1]})");
		}
	}

	private async Task EstablishSocks5Tunnel(Stream stream, CancellationToken ct)
	{
		await stream.WriteAsync(new byte[3] { 5, 1, 0 }, ct);
		byte[] array = await ReadExact(stream, 2, ct);
		if (array[0] != 5 || array[1] != 0)
		{
			throw new IOException("SOCKS5 unterstützt keine Verbindung ohne Anmeldung.");
		}
		byte[] bytes = Encoding.ASCII.GetBytes(_target.Host);
		if (bytes.Length > 255)
		{
			throw new InvalidOperationException("Zielname ist zu lang.");
		}
		using MemoryStream request = new MemoryStream();
		byte[] obj = new byte[5] { 5, 1, 0, 3, 0 };
		obj[4] = (byte)bytes.Length;
		request.Write(obj);
		request.Write(bytes);
		request.WriteByte((byte)(_target.Port >> 8));
		request.WriteByte((byte)(_target.Port & 0xFF));
		await stream.WriteAsync(request.ToArray(), ct);
		byte[] array2 = await ReadExact(stream, 4, ct);
		if (array2[0] != 5 || array2[1] != 0)
		{
			throw new IOException($"SOCKS5-Verbindung abgelehnt (Code {array2[1]})");
		}
		await ReadExact(stream, array2[3] switch
		{
			1 => 6, 
			4 => 18, 
			3 => (await ReadExact(stream, 1, ct))[0] + 2, 
			_ => throw new IOException("Ungültige SOCKS5-Antwort."), 
		}, ct);
	}

	private static async Task<string> ReadHeader(Stream stream, CancellationToken ct)
	{
		List<byte> bytes = new List<byte>(512);
		byte[] one = new byte[1];
		while (bytes.Count < 16384)
		{
			if (await stream.ReadAsync(one, ct) == 0)
			{
				throw new IOException("Verbindung vorzeitig beendet.");
			}
			bytes.Add(one[0]);
			int count = bytes.Count;
			if (count >= 4 && bytes[count - 4] == 13 && bytes[count - 3] == 10 && bytes[count - 2] == 13 && bytes[count - 1] == 10)
			{
				return Encoding.ASCII.GetString(bytes.ToArray());
			}
		}
		throw new IOException("Proxy-Antwort ist zu groß.");
	}

	private static async Task<string> ReadResponse(Stream stream, CancellationToken ct)
	{
		using MemoryStream output = new MemoryStream();
		byte[] buffer = new byte[4096];
		while (output.Length < 65536)
		{
			int num = await stream.ReadAsync(buffer, ct);
			if (num == 0)
			{
				break;
			}
			output.Write(buffer, 0, num);
		}
		return Encoding.UTF8.GetString(output.ToArray());
	}

	private static string ParseExitIp(string response)
	{
		int num = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);
		if (num < 0)
		{
			throw new IOException("Ungültige HTTP-Antwort.");
		}
		string text = response.Substring(0, num).Split("\r\n", 2)[0];
		if (!text.Contains(" 200 ", StringComparison.Ordinal))
		{
			throw new IOException("Testziel meldet " + text + ".");
		}
		int num2 = num + 4;
		if (!IPAddress.TryParse(response.Substring(num2, response.Length - num2).Trim(), out IPAddress address))
		{
			throw new IOException("Testantwort enthält keine gültige IP-Adresse.");
		}
		return address.ToString();
	}

	private static async Task<byte[]> ReadExact(Stream stream, int count, CancellationToken ct)
	{
		byte[] output = new byte[count];
		int num;
		for (int offset = 0; offset < count; offset += num)
		{
			num = await stream.ReadAsync(output.AsMemory(offset, count - offset), ct);
			if (num == 0)
			{
				throw new IOException("Verbindung vorzeitig beendet.");
			}
		}
		return output;
	}

	private static ProxyTestResult Failed(ProxyEndpoint proxy, Stopwatch stopwatch, string error)
	{
		stopwatch.Stop();
		return new ProxyTestResult
		{
			Proxy = proxy,
			IsWorking = false,
			ResponseTimeMs = stopwatch.ElapsedMilliseconds,
			Error = error
		};
	}

	private static string FriendlyError(Exception ex)
	{
		Exception baseException = ex.GetBaseException();
		if (!(baseException is SocketException))
		{
			if (!(baseException is AuthenticationException))
			{
				if (baseException is IOException ex2)
				{
					return (ex2.Message.Length <= 120) ? ex2.Message : (ex2.Message.Substring(0, 120) + "…");
				}
				return (baseException.Message.Length <= 120) ? baseException.Message : (baseException.Message.Substring(0, 120) + "…");
			}
			return "TLS-Prüfung fehlgeschlagen";
		}
		return "Nicht erreichbar";
	}
}
