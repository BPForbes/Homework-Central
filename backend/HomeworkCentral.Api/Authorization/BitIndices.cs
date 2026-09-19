namespace HomeworkCentral.Api.Authorization;

/// <summary>Mask A — moderation permissions (bit indices 0–255). Paired actions are bundled.</summary>
public static class ModerationPermissions
{
    public const short ViewReports = 0;
    public const short ResolveReports = 1;
    public const short WarnUser = 2;
    public const short TimeoutUser = 3;
    public const short MuteMembers = 4;
    public const short KickUser = 5;
    public const short BanMembers = 6;
    public const short DeleteMessages = 7;
    public const short EditMessages = 8;
    public const short PinMessages = 9;
    public const short LockChannels = 10;
    public const short ManageChannels = 11;
    public const short ManageRoles = 12;
    public const short ManagePermissions = 13;
    public const short ViewAuditLogs = 14;
    public const short ManageEvents = 15;
    public const short ManageSeminars = 16;
    public const short ModerateResources = 17;
    public const short SuspendAccounts = 18;
    public const short HandleAppeals = 19;
    public const short ManageServerInfrastructure = 20;
}

/// <summary>Mask B — platform roles. Higher bit index = higher authority for role grants.</summary>
public static class PlatformRoles
{
    public const short Guest = 0;
    public const short VerifiedUser = 1;
    public const short Student = 2;
    public const short Staff = 3;
    public const short Tutor = 4;
    public const short SeniorTutor = 5;
    public const short HeadTutor = 6;
    public const short Moderator = 7;
    public const short SeniorModerator = 8;
    public const short CommunityManager = 9;
    public const short EventOrganizer = 10;
    public const short SeminarHost = 11;
    public const short VerifiedEducator = 12;
    public const short Developer = 13;
    public const short BetaTester = 14;
    public const short Administrator = 15;
    public const short SystemAdministrator = 16;
    public const short BoardMember = 17;
    public const short Owner = 18;
    public const short Founder = 19;
    /// <summary>
    /// Cosmetic trial tutor badge. Stored at bit 20 but excluded from grant-authority ranking
    /// (see <see cref="PlatformRoleCatalog"/>); mutually exclusive with <see cref="Tutor"/>.
    /// </summary>
    public const short TrialTutor = 20;
}

/// <summary>Mask C — general subjects (bit indices 0–127).</summary>
public static class GeneralSubjects
{
    public const short Mathematics = 0;
    public const short Science = 1;
    public const short ComputerScience = 2;
    public const short Languages = 3;
    public const short History = 4;
    public const short Business = 5;
    public const short Art = 6;
    public const short Music = 7;
    public const short Engineering = 8;
    public const short Medicine = 9;
    public const short Finance = 10;
    public const short Economics = 11;
    public const short Education = 12;
}

/// <summary>Mask D — computer science expertise (bit indices 0–127).</summary>
public static class ComputerScienceExpertise
{
    public const short Python = 0;
    public const short C = 1;
    public const short CPlusPlus = 2;
    public const short CSharp = 3;
    public const short Java = 4;
    public const short JavaScript = 5;
    public const short TypeScript = 6;
    public const short Go = 7;
    public const short Rust = 8;
    public const short Kotlin = 9;
    public const short Swift = 10;
    public const short Html = 11;
    public const short Css = 12;
    public const short React = 13;
    public const short Angular = 14;
    public const short Vue = 15;
    public const short Backend = 16;
    public const short Frontend = 17;
    public const short RestApis = 18;
    public const short GraphQl = 19;
    public const short PostgreSql = 20;
    public const short MySql = 21;
    public const short MongoDb = 22;
    public const short Redis = 23;
    public const short Linux = 24;
    public const short Docker = 25;
    public const short Kubernetes = 26;
    public const short Azure = 27;
    public const short Aws = 28;
    public const short Networking = 29;
    public const short CyberSecurity = 30;
    public const short OperatingSystems = 31;
}

/// <summary>Mask E — mathematics expertise (bit indices 0–127).</summary>
public static class MathematicsExpertise
{
    public const short Algebra = 0;
    public const short Geometry = 1;
    public const short Trigonometry = 2;
    public const short Calculus = 3;
    public const short LinearAlgebra = 4;
    public const short DifferentialEquations = 5;
    public const short DiscreteMathematics = 6;
    public const short Statistics = 7;
    public const short Probability = 8;
    public const short NumericalMethods = 9;
}

/// <summary>Mask F — language expertise (bit indices 0–127).</summary>
public static class LanguageExpertise
{
    public const short English = 0;
    public const short Spanish = 1;
    public const short French = 2;
    public const short German = 3;
    public const short Italian = 4;
    public const short Portuguese = 5;
    public const short Japanese = 6;
    public const short Korean = 7;
    public const short Mandarin = 8;
    public const short Arabic = 9;
    public const short AmericanSignLanguage = 10;
}

/// <summary>Science expertise under general subject Science (bit indices 0–127).</summary>
public static class ScienceExpertise
{
    public const short Biology = 0;
    public const short Chemistry = 1;
    public const short Physics = 2;
    public const short Philosophy = 3;
    public const short Psychology = 4;
}

/// <summary>Mask G — platform features (bit indices 0–255).</summary>
public static class PlatformFeatures
{
    public const short PublicMessages = 0;
    public const short PrivateMessages = 1;
    public const short GroupMessages = 2;
    public const short VoiceRooms = 3;
    public const short VideoRooms = 4;
    public const short ScreenSharing = 5;
    public const short LivestreamHosting = 6;
    public const short LivestreamParticipation = 7;
    public const short SeminarHosting = 8;
    public const short SeminarUpload = 9;
    public const short EventPosting = 10;
    public const short CommunityPolls = 11;
    public const short ResourceSharing = 12;
    public const short WikiEditing = 13;
    public const short ProjectShowcase = 14;
    public const short FileUploads = 15;
    public const short ImageUploads = 16;
    public const short PublicProfile = 17;
    public const short ProfileCustomization = 18;
    public const short CommunityBadges = 19;
    public const short MarketplaceAccess = 20;
    public const short CommunityAnnouncements = 21;
    public const short EventCalendar = 22;
    public const short AiAssistant = 23;
    public const short AnalyticsDashboard = 24;
    public const short BetaFeatures = 25;
}

/// <summary>Mask H — account status (bit indices 0–63).</summary>
public static class AccountStatus
{
    public const short EmailVerified = 0;
    public const short PhoneVerified = 1;
    public const short IdentityVerified = 2;
    public const short BackgroundVerified = 3;
    public const short FeaturedTutor = 4;
    public const short FeaturedMember = 5;
    public const short TrustedContributor = 6;
    public const short GoodStanding = 7;
    public const short BetaMember = 8;
    public const short EarlyAccess = 9;
    public const short HonoraryMember = 10;
}

/// <summary>Subject mask category names stored on <see cref="Models.Subject.SubjectMask"/>.</summary>
public static class SubjectMaskNames
{
    public const string General = "General";
    public const string ComputerScience = "ComputerScience";
    public const string Mathematics = "Mathematics";
    public const string Languages = "Languages";
    public const string Science = "Science";
    public const string History = "History";
    public const string Business = "Business";
    public const string Art = "Art";
    public const string Music = "Music";
    public const string Engineering = "Engineering";
    public const string Medicine = "Medicine";
    public const string Finance = "Finance";
    public const string Economics = "Economics";
    public const string Education = "Education";
}
