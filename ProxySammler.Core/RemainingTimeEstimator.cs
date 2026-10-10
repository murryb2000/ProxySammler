using System.Diagnostics;

namespace ProxySammler.Core;

/// <summary>Schätzt die verbleibende Zeit des aktuellen, bekannten Arbeitsabschnitts.</summary>
public sealed class RemainingTimeEstimator
{
    private readonly Func<long> _timestamp;
    private string? _phase;
    private long _started;
    private int _lastDone;

    public RemainingTimeEstimator(Func<long>? timestamp = null)
    {
        _timestamp = timestamp ?? Stopwatch.GetTimestamp;
    }

    public string Update(string phase, int done, int total)
    {
        if (_phase != phase || done < _lastDone)
        {
            _phase = phase;
            _started = _timestamp();
            _lastDone = 0;
        }

        _lastDone = Math.Max(0, done);
        if (total <= 0) return "keine Einträge";
        if (done >= total) return "Abschnitt abgeschlossen";

        var elapsed = Stopwatch.GetElapsedTime(_started, _timestamp());
        if (done < 3 || elapsed < TimeSpan.FromSeconds(2))
            return "Restzeit wird ermittelt…";

        var remainingSeconds = elapsed.TotalSeconds * (total - done) / done;
        if (!double.IsFinite(remainingSeconds) || remainingSeconds < 0)
            return "Restzeit wird ermittelt…";
        if (remainingSeconds < 60)
            return $"noch ca. {Math.Max(1, (int)Math.Ceiling(remainingSeconds))} Sek.";
        return $"noch ca. {Math.Ceiling(remainingSeconds / 60):N0} Min.";
    }
}
