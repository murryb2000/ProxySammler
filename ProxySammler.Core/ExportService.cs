using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ProxySammler.Core;

public static class ExportService
{
	public static string ExportRun(string baseDirectory, IReadOnlyCollection<ProxyTestResult> results)
	{
		ProxyTestResult[] array = (from r in results
			where r.IsWorking
			orderby r.ResponseTimeMs, r.Proxy.Protocol
			select r).ToArray();
		string path = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
		string text = Path.Combine(baseDirectory, path);
		Directory.CreateDirectory(text);
		WriteText(Path.Combine(text, "proxies_all.txt"), array.Select((ProxyTestResult r) => r.Proxy.Url));
		WriteText(Path.Combine(text, "proxies_http.txt"), Addresses(array, ProxyProtocol.Http));
		WriteText(Path.Combine(text, "proxies_socks4.txt"), Addresses(array, ProxyProtocol.Socks4));
		WriteText(Path.Combine(text, "proxies_socks5.txt"), Addresses(array, ProxyProtocol.Socks5));
		WriteCsv(Path.Combine(text, "details.csv"), results);
		WriteText(Path.Combine(baseDirectory, "aktuell_all.txt"), array.Select((ProxyTestResult r) => r.Proxy.Url));
		WriteText(Path.Combine(baseDirectory, "aktuell_http.txt"), Addresses(array, ProxyProtocol.Http));
		WriteText(Path.Combine(baseDirectory, "aktuell_socks4.txt"), Addresses(array, ProxyProtocol.Socks4));
		WriteText(Path.Combine(baseDirectory, "aktuell_socks5.txt"), Addresses(array, ProxyProtocol.Socks5));
		WriteCsv(Path.Combine(baseDirectory, "aktuell_details.csv"), results);
		return text;
	}

	public static string ExportNewRun(string baseDirectory, IReadOnlyCollection<ProxyTestResult> results)
	{
		ProxyTestResult[] array = (from r in results
			where r.IsWorking
			orderby r.ResponseTimeMs, r.Proxy.Protocol
			select r).ToArray();
		string path = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_nur-neue";
		string text = Path.Combine(baseDirectory, path);
		Directory.CreateDirectory(text);
		WriteText(Path.Combine(text, "neue_proxies.txt"), array.Select((ProxyTestResult r) => r.Proxy.Url));
		WriteText(Path.Combine(text, "neue_http.txt"), Addresses(array, ProxyProtocol.Http));
		WriteText(Path.Combine(text, "neue_socks4.txt"), Addresses(array, ProxyProtocol.Socks4));
		WriteText(Path.Combine(text, "neue_socks5.txt"), Addresses(array, ProxyProtocol.Socks5));
		WriteCsv(Path.Combine(text, "neue_details.csv"), results);
		WriteText(Path.Combine(baseDirectory, "aktuell_neu.txt"), array.Select((ProxyTestResult r) => r.Proxy.Url));
		WriteText(Path.Combine(baseDirectory, "aktuell_neu_http.txt"), Addresses(array, ProxyProtocol.Http));
		WriteText(Path.Combine(baseDirectory, "aktuell_neu_socks4.txt"), Addresses(array, ProxyProtocol.Socks4));
		WriteText(Path.Combine(baseDirectory, "aktuell_neu_socks5.txt"), Addresses(array, ProxyProtocol.Socks5));
		WriteCsv(Path.Combine(baseDirectory, "aktuell_neu_details.csv"), results);
		return text;
	}

	public static string ExportPoolRetestRun(string baseDirectory, IReadOnlyCollection<ProxyTestResult> results)
	{
		ProxyTestResult[] array = (from r in results
			where r.IsWorking
			orderby r.ResponseTimeMs, r.Proxy.Protocol
			select r).ToArray();
		string path = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_listen-nachtest";
		string text = Path.Combine(baseDirectory, path);
		Directory.CreateDirectory(text);
		WriteText(Path.Combine(text, "weiterhin_funktionierend.txt"), array.Select((ProxyTestResult r) => r.Proxy.Url));
		WriteText(Path.Combine(text, "weiterhin_http.txt"), Addresses(array, ProxyProtocol.Http));
		WriteText(Path.Combine(text, "weiterhin_socks4.txt"), Addresses(array, ProxyProtocol.Socks4));
		WriteText(Path.Combine(text, "weiterhin_socks5.txt"), Addresses(array, ProxyProtocol.Socks5));
		WriteCsv(Path.Combine(text, "nachtest_details.csv"), results);
		return text;
	}

	public static void ExportTxt(string path, IEnumerable<ProxyTestResult> results)
	{
		WriteText(path, from r in results
			where r.IsWorking
			orderby r.ResponseTimeMs
			select r.Proxy.Url);
	}

	public static string ToIpPortText(IEnumerable<ProxyTestResult> results)
	{
		return string.Join(Environment.NewLine, (from r in results
			where r.IsWorking
			orderby r.ResponseTimeMs
			select r.Proxy.Address).Distinct<string>(StringComparer.OrdinalIgnoreCase));
	}

	private static IEnumerable<string> Addresses(IEnumerable<ProxyTestResult> results, ProxyProtocol protocol)
	{
		return from r in results
			where r.Proxy.Protocol == protocol
			select r.Proxy.Address;
	}

	private static void WriteText(string path, IEnumerable<string> lines)
	{
		File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
	}

	private static void WriteCsv(string path, IEnumerable<ProxyTestResult> results)
	{
		List<string> list = new List<string> { "Protokoll;Proxy;Status;Antwortzeit_ms;Ausgangs-IP;Fehler;Geprüft_UTC" };
		list.AddRange(from r in results
			orderby r.IsWorking descending, r.ResponseTimeMs
			select string.Join(';', Csv(r.Proxy.ProtocolName), Csv(r.Proxy.Address), r.IsWorking ? "OK" : "Fehler", r.ResponseTimeMs.ToString(), Csv(r.ExitIp), Csv(r.Error), r.CheckedAtUtc.ToString("O")));
		File.WriteAllLines(path, list, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
	}

	private static string Csv(string value)
	{
		return "\"" + value.Replace("\"", "\"\"") + "\"";
	}
}
