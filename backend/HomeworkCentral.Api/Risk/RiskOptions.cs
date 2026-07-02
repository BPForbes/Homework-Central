namespace HomeworkCentral.Api.Risk;

/// <summary>
/// Config-bound weights for <see cref="IRiskEngine"/>, section <c>"Risk"</c> in appsettings. Kept
/// as plain settable properties (not records) because <c>IOptions&lt;T&gt;</c> binding requires it.
/// All values are hand-tuned starting points, not calibrated against real attack traffic — expect
/// to retune once production logs show what actually gets flagged.
///
/// The <c>Score</c> this engine evaluates is now FCaptcha's trust score (see
/// <c>HomeworkCentral.Api.Captcha.FCaptcha.IFCaptchaVerifier</c>) rather than the old raw-telemetry
/// heuristic, and — critically — this engine is only ever consulted when that score is BELOW
/// <c>FCaptchaOptions.AllowTrustScore</c> (0.7 by default): anything at or above that threshold
/// already passed without reaching here. So every threshold below is calibrated against the 0..0.7
/// range, not 0..1 — a base threshold anywhere near 0.7 would make the fallback path nearly
/// impossible to clear even for a genuinely-solved puzzle.
/// </summary>
public sealed class RiskOptions
{
    /// <summary>Required score for a first-time signup captcha, before any adjustments. Lower than
    /// <see cref="VerifyRoleBaseThreshold"/> because signup is a lower-stakes action than granting
    /// an existing account an elevated role.</summary>
    public double RegisterBaseThreshold { get; set; } = 0.40;

    /// <summary>Required score for the dashboard "Verify" (Guest -> VerifiedUser) captcha.</summary>
    public double VerifyRoleBaseThreshold { get; set; } = 0.50;

    /// <summary>Added to the required threshold when the submission's IP doesn't match the IP the
    /// challenge was issued to. Deliberately not an outright reject — per the "don't block on one
    /// signal alone" rule, a strong-enough trust score can still clear the raised bar (e.g. a
    /// legitimate mobile network re-assigning an IP mid-session), but a marginal one won't.</summary>
    public double IpMismatchPenalty { get; set; } = 0.15;

    /// <summary>Added when this identity has never completed a scored captcha attempt before —
    /// brand-new identities get slightly less benefit of the doubt.</summary>
    public double NewIdentityPenalty { get; set; } = 0.05;

    /// <summary>Added per recent consecutive captcha failure from this identity, up to
    /// <see cref="MaxConsecutiveFailurePenalty"/> — each retry after a failure has to clear a
    /// higher bar, which is the escalating-friction behavior NIST recommends for repeated failed
    /// attempts.</summary>
    public double ConsecutiveFailurePenaltyPerFailure { get; set; } = 0.05;

    public double MaxConsecutiveFailurePenalty { get; set; } = 0.15;

    /// <summary>Scaling factor applied to the identity's current scraping-suspicion score (0..1,
    /// from <c>IScrapingDetectionService</c>) before adding it to the required threshold — request
    /// patterns across the whole session feed into the captcha bar, not just this one attempt.</summary>
    public double ScrapingSuspicionWeight { get; set; } = 0.20;

    /// <summary>Maximum amount a positive trust history can reduce the required threshold by.</summary>
    public double MaxTrustBonus { get; set; } = 0.10;

    /// <summary>The dynamic threshold is always clamped to this range — never so low that a single
    /// weak signal trivially passes, never so high that no realistic fallback attempt could clear
    /// it (the ceiling is deliberately kept below <c>FCaptchaOptions.AllowTrustScore</c>, since a
    /// score at or above that never reaches this engine in the first place).</summary>
    public double MinRequiredScore { get; set; } = 0.20;

    public double MaxRequiredScore { get; set; } = 0.68;

    /// <summary>Smoothing factor for the exponential moving average applied to an identity's trust
    /// score on each update: <c>newTrust = old*(1-alpha) + observed*alpha</c>. Small values mean a
    /// single attempt (good or bad) only nudges long-term trust rather than swinging it wildly.</summary>
    public double TrustEmaAlpha { get; set; } = 0.2;

    /// <summary>An identity's smoothed trust score is only updated at most once per this many
    /// seconds — rapid-fire retries (an attacker fishing for one lucky pass) don't get to move
    /// trust history as fast as a single well-spaced, genuine attempt would. Attempt counters
    /// (total/consecutive-failure) still update on every attempt regardless.</summary>
    public int MinProfileUpdateIntervalSeconds { get; set; } = 5;

    /// <summary>Trust decays back toward the neutral 0.5 baseline across long gaps of inactivity
    /// (half-life in hours), so trust built up long ago doesn't linger indefinitely on a dormant
    /// identity that then reappears.</summary>
    public int TrustDecayHalfLifeHours { get; set; } = 72;
}
