namespace ProxySammler.Core;

public enum CandidateStatus { Neu, Geprueft, Uebernommen, Ignoriert, Fehlerhaft }
public enum DiscoveryInterval { Taeglich, Woechentlich }

public sealed class DiscoverySettings
{
    public bool AutomaticSearch { get; set; }
    public DiscoveryInterval Interval { get; set; } = DiscoveryInterval.Taeglich;
    public bool AutoAcceptGood { get; set; }
    public DateTimeOffset? LastSearchUtc { get; set; }
    public DateTimeOffset? NextAllowedUtc { get; set; }

    public DateTimeOffset NextSearchUtc(DateTimeOffset now) =>
        (LastSearchUtc ?? now).Add(Interval == DiscoveryInterval.Taeglich ? TimeSpan.FromDays(1) : TimeSpan.FromDays(7));
}

public sealed class SourceCandidate
{
    public string Url { get; set; } = "";
    public string Provider { get; set; } = "GitHub";
    public ProxyProtocol Protocol { get; set; }
    public DateTimeOffset DiscoveredUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedUtc { get; set; }
    public int ProxyCount { get; set; }
    public int SampleCount { get; set; }
    public int WorkingCount { get; set; }
    public double SuccessRate => SampleCount == 0 ? 0 : (double)WorkingCount / SampleCount;
    public int Quality { get; set; }
    public CandidateStatus Status { get; set; } = CandidateStatus.Neu;
    public string LastError { get; set; } = "";
    public int ErrorCount { get; set; }
    public string ContentHash { get; set; } = "";
    public bool DuplicateContent { get; set; }
    public bool KnownSource { get; set; }
    public bool Reachable { get; set; }
}

public sealed record DiscoveredLink(string Url, string Provider, ProxyProtocol Protocol, DateTimeOffset? UpdatedUtc);
public sealed record DiscoverySummary(int Repositories, int Found, int New, int Known, int Checked, int Accepted, int Failed);

public static class DiscoveryPolicy
{
    public const int MaxDownloadBytes = 8 * 1024 * 1024;
    public const int MaxSampleSize = 12;
    public const int MaxParallelSources = 3;
    public const int MaxRedirects = 3;
    public const int AutoAcceptMinimumWorking = 1;
    public const int AutoAcceptMinimumQuality = 45;

    public static int Score(SourceCandidate c, DateTimeOffset now)
    {
        var score = c.Reachable ? 20 : 0;
        score += Math.Min(20, c.ProxyCount / 25);
        score += Math.Min(20, c.WorkingCount * 10);
        score += (int)Math.Round(c.SuccessRate * 20);
        if (c.UpdatedUtc is { } updated && now - updated <= TimeSpan.FromDays(30)) score += 10;
        score -= c.DuplicateContent ? 25 : 0;
        score -= c.KnownSource ? 20 : 0;
        score -= Math.Min(30, c.ErrorCount * 10);
        return Math.Clamp(score, 0, 100);
    }

    public static bool Suitable(SourceCandidate c) => c.Status == CandidateStatus.Geprueft &&
        c.Reachable && !c.KnownSource && !c.DuplicateContent &&
        c.WorkingCount >= AutoAcceptMinimumWorking && c.Quality >= AutoAcceptMinimumQuality;
}
