using System.Globalization;
using System.Text.Json;

namespace Ossuary.Tools.FetchCodexData;

/// <summary>
/// Paces requests against the limit Codex actually publishes, rather than one
/// hardcoded here and left to go stale.
/// </summary>
/// <remarks>
/// <para>
/// Their <c>/api/rate-limits</c> endpoint reports the live figures and, crucially,
/// that the scope is <b>per endpoint</b>:
/// </para>
/// <code>
///   {"browse":"300/minute",
///    "tiers":{"general":"15/minute","registered":"60/minute",
///             "academia":"100/minute","paid":"120/minute"},
///    "scope":"per endpoint"}
/// </code>
/// <para>
/// Per-endpoint scoping is why the two pipelines are so differently shaped. The
/// weekly ratings refresh spreads seven requests over six endpoints and never
/// uses more than a fraction of any one budget. The lift harvest sends every
/// request to <c>/api/draft-advice</c>, so the whole budget applies to it and
/// pacing is the entire problem.
/// </para>
/// <para>
/// Their written terms quote 60-120/minute; the live endpoint says 15 for an
/// unregistered caller. Where the two disagree the lower figure is the one to
/// respect — and it is the one that was almost certainly tripped during
/// development, when the API started refusing us mid-session.
/// </para>
/// </remarks>
internal sealed class RateLimit
{
    /// <summary>Used when the endpoint cannot be read. The most conservative tier.</summary>
    private const int FallbackPerMinute = 15;

    /// <summary>
    /// Headroom on the published rate. Limits are usually enforced over a
    /// sliding window, so pacing exactly at the boundary invites a 429 from
    /// ordinary jitter.
    /// </summary>
    private const double Margin = 1.15;

    private RateLimit(int perMinute, string source)
    {
        PerMinute = perMinute;
        Source = source;
        Delay = TimeSpan.FromSeconds(60.0 / perMinute * Margin);
    }

    internal int PerMinute { get; }

    internal string Source { get; }

    /// <summary>How long to wait between requests to one endpoint.</summary>
    internal TimeSpan Delay { get; }

    internal static async Task<RateLimit> Discover(HttpClient http, string baseUrl, string tier)
    {
        try
        {
            var body = await http.GetStringAsync($"{baseUrl}/api/rate-limits");
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("tiers", out var tiers)
                && tiers.TryGetProperty(tier, out var value)
                && Parse(value.GetString()) is { } perMinute)
            {
                return new RateLimit(perMinute, $"published '{tier}' tier");
            }

            return new RateLimit(FallbackPerMinute, $"no '{tier}' tier published; assuming the general tier");
        }
        catch (Exception ex)
        {
            return new RateLimit(FallbackPerMinute, $"could not read the published limits ({ex.GetType().Name}); assuming the general tier");
        }
    }

    /// <summary>Parses "15/minute".</summary>
    private static int? Parse(string? value)
    {
        if (value is null) return null;
        var slash = value.IndexOf('/');
        var head = slash < 0 ? value : value[..slash];
        return int.TryParse(head.Trim(), CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;
    }

    public override string ToString() =>
        $"{PerMinute}/minute per endpoint ({Source}) — {Delay.TotalSeconds:0.0}s between requests";
}
