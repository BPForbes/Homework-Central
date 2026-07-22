namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Fine-grained moderation concepts for ChatMonitor training and ticket monitoring.
/// Softmax targets these slugs; families support roll-up and related-concept hints on reports.
/// </summary>
public static class ChatMonitoringModerationConcepts
{
    public const string CatchAll = "moderation-general";

    public static readonly ModerationConceptDefinition[] All =
    [
        // 1. Financial solicitation and commercial abuse
        C("payment-solicitation", Families.Financial, "Asking another user to pay for help, answers, access, or assistance."),
        C("paywalled-help", Families.Financial, "Refusing to continue helping unless payment is provided."),
        C("tip-solicitation", Families.Financial, "Asking for, hinting at, or advertising tips after providing help."),
        C("tip-pressure", Families.Financial, "Repeatedly requesting or guilt-tripping someone into tipping."),
        C("off-platform-payment", Families.Financial, "Redirecting users to Cash App, PayPal, cryptocurrency, gift cards, or another external payment channel."),
        C("payment-link-abuse", Families.Financial, "Sending unsolicited, misleading, or disguised payment links or QR codes."),
        C("financial-credential-request", Families.Financial, "Requesting card numbers, bank credentials, PINs, payment OTPs, or similar information."),
        C("donation-misrepresentation", Families.Financial, "Inventing or misrepresenting an emergency, charity, or hardship to obtain money."),
        C("refund-chargeback-fraud", Families.Financial, "Coordinating fraudulent refunds, reversals, or chargebacks."),
        C("paid-privilege-offer", Families.Financial, "Offering priority, rankings, moderation influence, private access, or platform privileges for money."),

        // 2. Fraud, deception, and impersonation
        C("user-impersonation", Families.Fraud, "Pretending to be another community member."),
        C("staff-impersonation", Families.Fraud, "Pretending to be a moderator, administrator, tutor, employee, or platform representative."),
        C("qualification-misrepresentation", Families.Fraud, "Falsely claiming credentials, licenses, degrees, experience, or expertise."),
        C("affiliation-misrepresentation", Families.Fraud, "Falsely claiming association with an organization, school, employer, or government agency."),
        C("fabricated-testimonial", Families.Fraud, "Creating fake endorsements, reviews, success stories, or references."),
        C("fabricated-evidence", Families.Fraud, "Producing or modifying supposed screenshots, messages, receipts, or records to mislead others."),
        C("phishing-pretext", Families.Fraud, "Inventing an account, security, employment, or support problem to collect information."),
        C("bait-and-switch", Families.Fraud, "Offering one service, answer, item, or condition and later substituting another."),
        C("trust-building-scam", Families.Fraud, "Developing a relationship or dependency as preparation for financial or personal exploitation."),
        C("marketplace-fraud", Families.Fraud, "Misrepresenting an item, service, transaction, delivery, refund, or ownership claim."),

        // 3. Privacy and personal-data misuse
        C("doxxing", Families.Privacy, "Publishing or threatening to publish identifying information without permission."),
        C("personal-data-solicitation", Families.Privacy, "Unnecessarily requesting names, addresses, phone numbers, birth dates, or identifying documents."),
        C("sensitive-data-exposure", Families.Privacy, "Sharing medical, financial, employment, educational, or legal information without authorization."),
        C("contact-harvesting", Families.Privacy, "Collecting usernames, emails, phone numbers, or social accounts for later targeting."),
        C("precise-location-solicitation", Families.Privacy, "Requesting a home address, live location, workplace, school, or regular route without a legitimate need."),
        C("location-tracking", Families.Privacy, "Attempting to determine or continuously monitor another person’s physical location."),
        C("private-message-leak", Families.Privacy, "Publishing private conversations without permission or a valid safety justification."),
        C("nonconsensual-image-sharing", Families.Privacy, "Distributing someone’s photographs or videos without permission."),
        C("unauthorized-recording-distribution", Families.Privacy, "Sharing private audio, screen recordings, calls, or meetings without authorization."),
        C("personal-data-sale", Families.Privacy, "Offering to buy, sell, trade, or commercially distribute personal information."),

        // 4. Sexual misconduct and consent violations
        C("sexual-solicitation", Families.Sexual, "Requesting sexual activity, sexual conversation, or sexual interaction where it is prohibited or unwanted."),
        C("unwanted-sexual-content", Families.Sexual, "Sending sexual text, imagery, links, or descriptions without consent."),
        C("explicit-image-request", Families.Sexual, "Asking another user to provide nude or sexually explicit material."),
        C("nonconsensual-intimate-media", Families.Sexual, "Sharing, threatening to share, or requesting intimate material distributed without consent."),
        C("sexual-coercion", Families.Sexual, "Using pressure, authority, manipulation, dependency, or consequences to obtain sexual compliance."),
        C("sextortion", Families.Sexual, "Demanding money, content, access, or actions under threat of exposing intimate material."),
        C("sexualized-commentary", Families.Sexual, "Repeatedly evaluating or discussing another user’s body or sexual characteristics without invitation."),
        C("fetishized-targeting", Families.Sexual, "Targeting a person or identity group with unwanted fetishizing statements or requests."),
        C("sexual-service-advertising", Families.Sexual, "Offering, requesting, or arranging prohibited sexual services."),
        C("consent-boundary-violation", Families.Sexual, "Continuing sexual or intimate behavior after refusal, discomfort, blocking, or withdrawal of consent."),

        // 5. Minor safety and grooming indicators
        C("minor-targeting", Families.MinorSafety, "Deliberately selecting suspected minors for inappropriate personal or sexual interaction."),
        C("age-probing-for-sexual-purpose", Families.MinorSafety, "Asking about age as part of escalating toward sexual conversation or content."),
        C("private-channel-migration-with-minor", Families.MinorSafety, "Pressuring a minor to move from public chat to private messages or another platform."),
        C("secrecy-demand-to-minor", Families.MinorSafety, "Instructing a minor to hide conversations, gifts, meetings, or relationships from trusted adults."),
        C("gift-or-payment-offer-to-minor", Families.MinorSafety, "Offering money, gifts, game items, subscriptions, or opportunities to gain access or compliance."),
        C("meetup-request-with-minor", Families.MinorSafety, "Attempting to arrange an in-person meeting with a minor under unsafe circumstances."),
        C("minor-explicit-content-request", Families.MinorSafety, "Requesting sexual or explicit material from someone believed to be underage."),
        C("child-exploitation-content-sharing", Families.MinorSafety, "Sharing, requesting, advertising, or facilitating child sexual exploitation material."),
        C("minor-personal-data-request", Families.MinorSafety, "Requesting a minor’s school, address, schedule, phone number, or live location."),
        C("grooming-escalation", Families.MinorSafety, "A longitudinal pattern of trust-building, isolation, secrecy, gifts, boundary testing, and sexual escalation."),

        // 6. Physical safety, coercion, and criminal targeting
        C("violent-intent", Families.PhysicalSafety, "Expressing a concrete desire or plan to physically injure a person or group."),
        C("attack-planning", Families.PhysicalSafety, "Discussing operational steps, timing, access, transportation, or coordination for an attack."),
        C("target-selection", Families.PhysicalSafety, "Identifying or evaluating people, places, events, or facilities as possible attack targets."),
        C("weapon-acquisition-facilitation", Families.PhysicalSafety, "Helping someone obtain weapons for an indicated harmful or criminal purpose."),
        C("stalking", Families.PhysicalSafety, "Repeatedly tracking, contacting, observing, or appearing near another person against their wishes."),
        C("swatting", Families.PhysicalSafety, "Making or coordinating a false emergency report intended to send armed responders to someone."),
        C("extortion", Families.PhysicalSafety, "Demanding money, access, property, silence, or action through coercive consequences."),
        C("blackmail", Families.PhysicalSafety, "Demanding compliance by threatening to reveal damaging or private information."),
        C("trafficking-recruitment", Families.PhysicalSafety, "Recruiting, transporting, controlling, or exploiting people through force, fraud, or coercion."),
        C("coercive-meetup", Families.PhysicalSafety, "Pressuring someone to meet, travel, enter a vehicle, or remain somewhere against their wishes."),

        // 7. Cybersecurity and account abuse
        C("malicious-link-distribution", Families.Cybersecurity, "Sending links intended to steal information, install software, redirect deceptively, or compromise a device."),
        C("credential-theft", Families.Cybersecurity, "Attempting to obtain another user’s password, recovery code, API key, or authentication secret."),
        C("account-takeover", Families.Cybersecurity, "Attempting to gain control of another person’s account."),
        C("session-token-theft", Families.Cybersecurity, "Requesting, collecting, or using cookies, tokens, session identifiers, or authentication artifacts."),
        C("malware-distribution", Families.Cybersecurity, "Sharing or deploying malicious executables, scripts, packages, documents, or payloads."),
        C("malicious-remote-access", Families.Cybersecurity, "Convincing users to install remote-control software for unauthorized access or fraud."),
        C("unauthorized-access-guidance", Families.Cybersecurity, "Providing targeted assistance for accessing systems, accounts, or data without authorization."),
        C("denial-of-service-coordination", Families.Cybersecurity, "Organizing traffic or resource exhaustion intended to disrupt a service."),
        C("botnet-recruitment", Families.Cybersecurity, "Recruiting devices, accounts, or users into coordinated malicious automation."),
        C("security-control-bypass", Families.Cybersecurity, "Assisting with defeating authentication, authorization, licensing, monitoring, or protective controls without authorization."),

        // 8. Platform manipulation and reporting abuse
        C("vote-manipulation", Families.PlatformAbuse, "Artificially increasing or decreasing votes, ratings, reactions, or reputation."),
        C("coordinated-brigading", Families.PlatformAbuse, "Organizing a group to overwhelm, target, downvote, report, or disrupt a user or discussion."),
        C("sockpuppet-coordination", Families.PlatformAbuse, "Operating multiple accounts to create false consensus or conceal coordinated behavior."),
        C("fake-engagement", Families.PlatformAbuse, "Purchasing, exchanging, scripting, or manufacturing reactions, followers, replies, or views."),
        C("false-reporting", Families.PlatformAbuse, "Knowingly filing materially false moderation reports."),
        C("mass-report-coordination", Families.PlatformAbuse, "Organizing multiple users or accounts to report a target regardless of actual conduct."),
        C("evidence-tampering", Families.PlatformAbuse, "Deleting, altering, cropping, or fabricating evidence intended for a moderation decision."),
        C("report-retaliation", Families.PlatformAbuse, "Punishing, exposing, targeting, or intimidating someone believed to have submitted a report."),
        C("witness-coaching", Families.PlatformAbuse, "Pressuring other users to provide a specific false or misleading account of an incident."),
        C("moderator-action-interference", Families.PlatformAbuse, "Attempting to bribe, manipulate, deceive, overwhelm, or improperly influence moderation personnel."),

        // 9. Discrimination, social coercion, and reputational abuse
        C("protected-class-targeting", Families.Discrimination, "Directing hostility or degrading treatment at someone because of a protected characteristic."),
        C("protected-class-exclusion", Families.Discrimination, "Attempting to exclude users from participation based on a protected characteristic."),
        C("dehumanization", Families.Discrimination, "Describing a person or group as subhuman, diseased, contaminating, or inherently unworthy."),
        C("identity-stereotyping", Families.Discrimination, "Assigning behavior, competence, morality, or intent based primarily on identity-group stereotypes."),
        C("coercive-demand", Families.Discrimination, "Using intimidation, authority, dependency, or social consequences to force compliance."),
        C("persistent-unwanted-contact", Families.Discrimination, "Continuing to message, tag, follow, or contact someone after clear refusal or blocking."),
        C("dogpiling", Families.Discrimination, "Encouraging multiple users to repeatedly confront, mock, or pressure one target."),
        C("reputational-sabotage", Families.Discrimination, "Deliberately attempting to damage another user’s standing through deceptive or malicious conduct."),
        C("malicious-rumor-spreading", Families.Discrimination, "Repeating unverified damaging claims as fact with the apparent purpose of harming someone."),
        C("public-shaming", Families.Discrimination, "Organizing or publishing humiliating material intended to expose a user to ridicule or social punishment."),

        // 10. Dangerous misinformation and deceptive content
        C("medical-misinformation", Families.Misinformation, "Presenting materially dangerous false medical claims as reliable professional guidance."),
        C("financial-misinformation", Families.Misinformation, "Presenting materially false or deceptive financial claims likely to cause significant loss."),
        C("legal-misinformation", Families.Misinformation, "Knowingly presenting materially false legal claims as authoritative advice in a consequential situation."),
        C("fabricated-source", Families.Misinformation, "Inventing citations, quotations, experts, research, court decisions, or documentation."),
        C("manipulated-media-evidence", Families.Misinformation, "Presenting altered, selectively edited, or falsely captioned media as authentic evidence."),
        C("false-emergency-report", Families.Misinformation, "Fabricating an emergency, missing person, disaster, active danger, or urgent safety event."),
        C("dangerous-challenge-promotion", Families.Misinformation, "Encouraging users to participate in a challenge likely to cause serious injury or property damage."),
        C("unsafe-substance-instructions", Families.Misinformation, "Providing hazardous substance-use instructions in a context indicating likely misuse or injury."),
        C("coordinated-disinformation", Families.Misinformation, "Participating in organized distribution of materially deceptive narratives through multiple users or accounts."),
        C("synthetic-evidence-misrepresentation", Families.Misinformation, "Presenting AI-generated voices, images, videos, messages, or documents as authentic evidence."),
    ];

    public static IReadOnlyList<string> Slugs { get; } = All.Select(c => c.Slug).ToArray();

    public static IReadOnlyList<string> SoftmaxLabels { get; } = [.. Slugs, CatchAll];

    public static bool TryGet(string slug, out ModerationConceptDefinition concept)
    {
        foreach (ModerationConceptDefinition item in All)
        {
            if (string.Equals(item.Slug, slug, StringComparison.OrdinalIgnoreCase))
            {
                concept = item;
                return true;
            }
        }

        concept = default!;
        return false;
    }

    public static string FamilyOf(string slug) =>
        TryGet(slug, out ModerationConceptDefinition concept) ? concept.Family : Families.General;

    /// <summary>Sibling concepts in the same family (excludes the seed slug).</summary>
    public static IReadOnlyList<string> RelatedConcepts(string slug, int max = 8)
    {
        if (!TryGet(slug, out ModerationConceptDefinition seed))
            return [];
        return All
            .Where(c => c.Family == seed.Family && !string.Equals(c.Slug, seed.Slug, StringComparison.Ordinal))
            .Select(c => c.Slug)
            .Take(Math.Clamp(max, 1, 20))
            .ToArray();
    }

    /// <summary>Maps legacy broad moderation labels onto the fine-grained vocabulary.</summary>
    public static string MapLegacyBroadLabel(string value) => value switch
    {
        "spam" or "moderation-spam" or "flood" => "fake-engagement",
        "profanity" or "moderation-profanity" or "cussing" or "cuss" => CatchAll,
        "threat" or "moderation-threat" or "harm" => "violent-intent",
        "harassment" or "moderation-harassment" or "insult" or "abuse" => "persistent-unwanted-contact",
        "evasion" or "moderation-evasion" or "filter" => "security-control-bypass",
        "moderation-general" or "general" => CatchAll,
        _ => value,
    };

    public static class Families
    {
        public const string Financial = "financial-solicitation-commercial-abuse";
        public const string Fraud = "fraud-deception-impersonation";
        public const string Privacy = "privacy-personal-data-misuse";
        public const string Sexual = "sexual-misconduct-consent";
        public const string MinorSafety = "minor-safety-grooming";
        public const string PhysicalSafety = "physical-safety-coercion";
        public const string Cybersecurity = "cybersecurity-account-abuse";
        public const string PlatformAbuse = "platform-manipulation-reporting-abuse";
        public const string Discrimination = "discrimination-social-coercion";
        public const string Misinformation = "dangerous-misinformation";
        public const string General = "moderation-general";
    }

    private static ModerationConceptDefinition C(string slug, string family, string meaning) =>
        new(slug, family, meaning);
}

public sealed record ModerationConceptDefinition(string Slug, string Family, string Meaning);
