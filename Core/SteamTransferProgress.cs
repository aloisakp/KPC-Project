using System.Globalization;
using System.Text.RegularExpressions;

namespace KpcLauncher.Core;

/// <summary>
/// Estimates compressed transfer bytes from Steam's periodic, client-wide rate samples.
/// File lengths are deliberately not used: Steam preallocates them. This is display-only;
/// only the matching console completion message may finish a download.
/// </summary>
internal sealed partial class SteamTransferProgress(uint appId, uint depotId, ulong manifestId)
{
    private static readonly TimeSpan RateLifetime = TimeSpan.FromSeconds(75);
    private long _total;
    private double _bytes;
    private double _rate;
    private DateTime _started;
    private DateTime _integratedTo;
    private DateTime _rateAt;
    private bool _hasRate;
    private bool _matchedDepot;
    private bool _ambiguous;

    public DateTime LastActivity { get; private set; }

    [GeneratedRegex(@"AppID (?<app>\d+) update started : download (?<done>\d+)/(?<total>\d+)")]
    private static partial Regex TotalPattern();

    [GeneratedRegex(@"Downloading \d+ chunks for depot (?<depot>\d+) \((?<manifest>\d+)\)")]
    private static partial Regex DepotPattern();

    [GeneratedRegex(@"Current download rate: (?<mbps>[\d.]+) Mbps")]
    private static partial Regex RatePattern();

    public void Observe(string line, DateTime now)
    {
        var at = now;
        if (line.StartsWith('['))
        {
            if (line.Length < 21 || line[20] != ']' ||
                !DateTime.TryParseExact(line.AsSpan(1, 19), "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out at) || at > now.AddSeconds(2)) return;
        }

        if (TotalPattern().Match(line) is { Success: true } transfer)
        {
            if (!uint.TryParse(transfer.Groups["app"].Value, out var app)) return;
            if (app != appId || _matchedDepot)
            {
                // Client-wide rates cannot distinguish simultaneous transfers, including
                // another manifest of this app. Stop estimating for the rest of this run.
                _ambiguous = true;
                return;
            }
            if (!long.TryParse(transfer.Groups["total"].Value, out var total) || total <= 0 ||
                !long.TryParse(transfer.Groups["done"].Value, out var done) || done > total) return;
            _total = total;
            _bytes = done;
            _started = _integratedTo = at;
            return;
        }

        if (DepotPattern().Match(line) is { Success: true } depot)
        {
            if (!uint.TryParse(depot.Groups["depot"].Value, out var id) ||
                !ulong.TryParse(depot.Groups["manifest"].Value, out var manifest)) return;
            if (id == depotId && manifest == manifestId) _matchedDepot = true;
            else _ambiguous = true;
            return;
        }

        if (!_matchedDepot || _ambiguous || _total <= 0 || (_hasRate && at < _rateAt) ||
            RatePattern().Match(line) is not { Success: true } sample ||
            !double.TryParse(sample.Groups["mbps"].Value, CultureInfo.InvariantCulture, out var mbps) ||
            !double.IsFinite(mbps) || mbps < 0) return;

        Advance(at);
        _rate = mbps * 1_000_000 / 8;
        if (!_hasRate)
        {
            // The first reading summarizes activity since the transfer began. Do not
            // extrapolate across an arbitrarily long startup or log gap.
            _bytes += _rate * Math.Clamp((at - _started).TotalSeconds, 0, RateLifetime.TotalSeconds);
        }
        _hasRate = true;
        _rateAt = at;
        if (at > _integratedTo) _integratedTo = at;
        if (_rate > 0) LastActivity = at;
    }

    private void Advance(DateTime now)
    {
        if (!_hasRate || _ambiguous || now <= _integratedTo) return;
        var end = now < _rateAt + RateLifetime ? now : _rateAt + RateLifetime;
        if (end > _integratedTo) _bytes += _rate * (end - _integratedTo).TotalSeconds;
        _integratedTo = now;
        // Bound the estimate itself, so a long stale sample cannot keep it pinned later.
        _bytes = Math.Clamp(_bytes, 0, _total * .95);
    }

    public StepProgress Report(string label, DateTime now)
    {
        Advance(now);
        if (_ambiguous || !_matchedDepot || _total <= 0)
            return new StepProgress($"Downloading {label}", 0, 0, _ambiguous
                ? "Steam is downloading - percentage unavailable during overlapping downloads"
                : "Waiting for Steam's transfer details");

        var done = (long)Math.Clamp(_bytes, 0, _total * .95);
        var percent = 100.0 * done / _total;
        var detail = $"Estimated {percent:0}% - {Human.Bytes(done)} of {Human.Bytes(_total)}";
        if (percent >= 94.9) detail += " - waiting for Steam completion";
        else if (_hasRate && _rate > 0 && now - _rateAt <= RateLifetime)
            detail += $" - Steam: {_rate * 8 / 1_000_000:0} Mbps";
        else detail += " - waiting for Steam activity";
        return new StepProgress($"Downloading {label}", done, _total, detail);
    }
}
