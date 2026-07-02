namespace HomeworkCentral.Api.ScrapingDetection;

/// <summary>One HTTP request, reduced to the shape the detector cares about. <see cref="Identity"/>
/// is <c>user:{userId}</c> for authenticated callers, else <c>ip:{address}</c>.</summary>
public sealed record ScrapingRequestSample(string Identity, string Path, string Method, DateTime TimestampUtc);

/// <summary><see cref="Reason"/> is a semicolon-joined list of the signals that fired, for logging only.</summary>
public sealed record ScrapingAssessment(double SuspicionScore, bool ShouldBlock, string? Reason);

/// <summary>
/// Cross-cutting anomaly detector that runs on every API request (captcha, chat, subjects, auth,
/// ...) regardless of endpoint, looking for request-pattern signatures of automated data scraping
/// rather than normal browsing: sustained high request rate, enumerating many distinct resources,
/// an almost entirely read-only request mix, and suspiciously uniform request timing. This is a
/// separate, broader concern from <c>IBehaviorScoringService</c>, which only scores a single
/// captcha attempt's mouse/keyboard telemetry — this one watches the whole session/identity over
/// time across every route.
/// </summary>
public interface IScrapingDetectionService
{
    ScrapingAssessment RecordRequest(ScrapingRequestSample sample);
}
