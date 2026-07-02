namespace HomeworkCentral.Api.Risk;

/// <summary>A snapshot of an identity's captcha history. <see cref="TrustScore"/> is a slow-moving
/// exponential average of past attempt scores (0 = consistently bot-like, 1 = consistently
/// human-like), distinct from any single attempt's own behavioral score.</summary>
public sealed record IdentityRiskProfile(
    double TrustScore,
    int TotalScans,
    int SuccessfulScans,
    int ConsecutiveFailures,
    DateTime LastUpdatedUtc);

/// <summary>
/// Tracks a rolling trust profile per identity (<c>user:{id}</c> or <c>ip:{address}</c>, see
/// <c>HomeworkCentral.Api.Security.RequestIdentity</c>) across captcha attempts, so
/// <see cref="IRiskEngine"/> can raise or lower the required threshold based on that identity's
/// track record rather than treating every attempt as a first attempt.
/// </summary>
public interface IIdentityRiskProfileService
{
    IdentityRiskProfile GetProfile(string identity);

    /// <summary>Records the outcome of one scored attempt. Attempt counters always update; the
    /// smoothed <see cref="IdentityRiskProfile.TrustScore"/> only updates if enough time has passed
    /// since its last update (see <see cref="RiskOptions.MinProfileUpdateIntervalSeconds"/>) — "we
    /// do not always update the value."</summary>
    void RecordOutcome(string identity, bool passed, double observedScore);
}
