using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using HomeworkCentral.Api.Authorization;

namespace HomeworkCentral.Api.Data;

/// <summary>
/// Static authorization catalog with deterministic GUIDs, precomputed connection-table ties,
/// and role bitmasks. Used to seed every tenant database without per-run randomness.
/// </summary>
public static class AuthorizationCatalog
{
    private static readonly short[] AllModerationPermissions =
        Enumerable.Range(0, ModerationPermissions.ManageServerInfrastructure + 1).Select(i => (short)i).ToArray();

    public sealed record RoleDefinition(string Name, string Description, short[] PermissionIds)
    {
        public Guid RoleId => AuthorizationGuids.Role(Name);
    }

    public sealed record PermissionDefinition(short PermissionId, string Name, string Description);

    public sealed record SubjectDefinition(
        string Name,
        string SubjectMask,
        short BitIndex,
        string? ParentName = null)
    {
        public Guid SubjectId => AuthorizationGuids.Subject(SubjectMask, BitIndex);
    }

    public sealed record RolePermissionTie(Guid RoleId, short PermissionId);

    public static IReadOnlyList<PermissionDefinition> Permissions { get; } =
    [
        new(ModerationPermissions.ViewReports, "ViewReports", "View moderation reports."),
        new(ModerationPermissions.ResolveReports, "ResolveReports", "Resolve moderation reports."),
        new(ModerationPermissions.WarnUser, "WarnUser", "Issue warnings to members."),
        new(ModerationPermissions.TimeoutUser, "TimeoutUser", "Timeout members."),
        new(ModerationPermissions.MuteMembers, "MuteMembers", "Mute and unmute members."),
        new(ModerationPermissions.KickUser, "KickUser", "Kick members from sessions."),
        new(ModerationPermissions.BanMembers, "BanMembers", "Ban and unban members."),
        new(ModerationPermissions.DeleteMessages, "DeleteMessages", "Delete messages."),
        new(ModerationPermissions.EditMessages, "EditMessages", "Edit messages."),
        new(ModerationPermissions.PinMessages, "PinMessages", "Pin messages."),
        new(ModerationPermissions.LockChannels, "LockChannels", "Lock and unlock channels."),
        new(ModerationPermissions.ManageChannels, "ManageChannels", "Manage channels."),
        new(ModerationPermissions.ManageRoles, "ManageRoles", "Grant and revoke platform roles."),
        new(ModerationPermissions.ManagePermissions, "ManagePermissions", "Manage permission assignments."),
        new(ModerationPermissions.ViewAuditLogs, "ViewAuditLogs", "View audit logs."),
        new(ModerationPermissions.ManageEvents, "ManageEvents", "Create and manage community events."),
        new(ModerationPermissions.ManageSeminars, "ManageSeminars", "Host and upload seminars."),
        new(ModerationPermissions.ModerateResources, "ModerateResources", "Moderate shared resources."),
        new(ModerationPermissions.SuspendAccounts, "SuspendAccounts", "Suspend and restore accounts."),
        new(ModerationPermissions.HandleAppeals, "HandleAppeals", "Handle moderation appeals."),
        new(ModerationPermissions.ManageServerInfrastructure, "ManageServerInfrastructure", "Manage server infrastructure."),
    ];

    public static IReadOnlyList<RoleDefinition> Roles { get; } =
    [
        new("Guest", "Unauthenticated or newly registered visitor.", []),
        new("VerifiedUser", "Verified community member.", []),
        new("Student", "Student participant.", []),
        new("Staff", "Platform staff member.", []),
        new("Tutor", "Homework tutor.", []),
        new("TrialTutor", "Cosmetic trial tutor badge (mentionable; mutually exclusive with Tutor).", []),
        new("SeniorTutor", "Senior tutor with seminar and event responsibilities.",
        [
            ModerationPermissions.ManageSeminars,
            ModerationPermissions.ManageEvents,
        ]),
        new("HeadTutor", "Head tutor with appeals and role management responsibilities.",
        [
            ModerationPermissions.HandleAppeals,
            ModerationPermissions.ManageRoles,
            ModerationPermissions.ManageSeminars,
            ModerationPermissions.ManageEvents,
        ]),
        new("Moderator", "Community moderator.",
        [
            ModerationPermissions.ViewReports,
            ModerationPermissions.DeleteMessages,
            ModerationPermissions.MuteMembers,
        ]),
        new("SeniorModerator", "Senior community moderator.",
        [
            ModerationPermissions.ViewReports,
            ModerationPermissions.ResolveReports,
            ModerationPermissions.WarnUser,
            ModerationPermissions.TimeoutUser,
            ModerationPermissions.MuteMembers,
            ModerationPermissions.DeleteMessages,
            ModerationPermissions.PinMessages,
            ModerationPermissions.ModerateResources,
            ModerationPermissions.KickUser,
            ModerationPermissions.LockChannels,
        ]),
        new("CommunityManager", "Community manager.", []),
        new("EventOrganizer", "Event organizer.", [ModerationPermissions.ManageEvents]),
        new("SeminarHost", "Seminar host.", [ModerationPermissions.ManageSeminars]),
        new("VerifiedEducator", "Verified educator.", []),
        new("Developer", "Platform developer.", []),
        new("BetaTester", "Beta program tester.", []),
        new("Administrator", "Platform administrator.",
        [
            ModerationPermissions.ViewReports,
            ModerationPermissions.ResolveReports,
            ModerationPermissions.WarnUser,
            ModerationPermissions.TimeoutUser,
            ModerationPermissions.MuteMembers,
            ModerationPermissions.KickUser,
            ModerationPermissions.BanMembers,
            ModerationPermissions.DeleteMessages,
            ModerationPermissions.ManageRoles,
            ModerationPermissions.ViewAuditLogs,
            ModerationPermissions.ManageEvents,
            ModerationPermissions.ManageSeminars,
            ModerationPermissions.ManageServerInfrastructure,
        ]),
        new("SystemAdministrator", "System administrator.",
        [
            ModerationPermissions.ViewReports,
            ModerationPermissions.ResolveReports,
            ModerationPermissions.WarnUser,
            ModerationPermissions.TimeoutUser,
            ModerationPermissions.MuteMembers,
            ModerationPermissions.KickUser,
            ModerationPermissions.BanMembers,
            ModerationPermissions.DeleteMessages,
            ModerationPermissions.EditMessages,
            ModerationPermissions.PinMessages,
            ModerationPermissions.LockChannels,
            ModerationPermissions.ManageChannels,
            ModerationPermissions.ManageRoles,
            ModerationPermissions.ViewAuditLogs,
            ModerationPermissions.ManageEvents,
            ModerationPermissions.ManageSeminars,
            ModerationPermissions.ModerateResources,
            ModerationPermissions.SuspendAccounts,
            ModerationPermissions.HandleAppeals,
            ModerationPermissions.ManageServerInfrastructure,
        ]),
        new("BoardMember", "Board member.",
        [
            ModerationPermissions.ViewReports,
            ModerationPermissions.ViewAuditLogs,
            ModerationPermissions.HandleAppeals,
            ModerationPermissions.ManageRoles,
        ]),
        new("Owner", "Platform owner.", AllModerationPermissions),
        new("Founder", "Platform founder.", AllModerationPermissions),
    ];

    public static IReadOnlyList<SubjectDefinition> Subjects { get; } =
    [
        new("Science", SubjectMaskNames.General, GeneralSubjects.Science),
        new("Computer Science", SubjectMaskNames.General, GeneralSubjects.ComputerScience),
        new("Mathematics", SubjectMaskNames.General, GeneralSubjects.Mathematics),
        new("Languages", SubjectMaskNames.General, GeneralSubjects.Languages),
        new("History", SubjectMaskNames.General, GeneralSubjects.History),
        new("Business", SubjectMaskNames.General, GeneralSubjects.Business),
        new("Art", SubjectMaskNames.General, GeneralSubjects.Art),
        new("Music", SubjectMaskNames.General, GeneralSubjects.Music),
        new("Engineering", SubjectMaskNames.General, GeneralSubjects.Engineering),
        new("Medicine", SubjectMaskNames.General, GeneralSubjects.Medicine),
        new("Finance", SubjectMaskNames.General, GeneralSubjects.Finance),
        new("Economics", SubjectMaskNames.General, GeneralSubjects.Economics),
        new("Education", SubjectMaskNames.General, GeneralSubjects.Education),
        new("Biology", SubjectMaskNames.Science, ScienceExpertise.Biology, "Science"),
        new("Chemistry", SubjectMaskNames.Science, ScienceExpertise.Chemistry, "Science"),
        new("Physics", SubjectMaskNames.Science, ScienceExpertise.Physics, "Science"),
        new("Philosophy", SubjectMaskNames.Science, ScienceExpertise.Philosophy, "Science"),
        new("Psychology", SubjectMaskNames.Science, ScienceExpertise.Psychology, "Science"),
        new("Python", SubjectMaskNames.ComputerScience, ComputerScienceExpertise.Python, "Computer Science"),
        new("C#", SubjectMaskNames.ComputerScience, ComputerScienceExpertise.CSharp, "Computer Science"),
        new("Backend", SubjectMaskNames.ComputerScience, ComputerScienceExpertise.Backend, "Computer Science"),
        new("Docker", SubjectMaskNames.ComputerScience, ComputerScienceExpertise.Docker, "Computer Science"),
        new("Algebra", SubjectMaskNames.Mathematics, MathematicsExpertise.Algebra, "Mathematics"),
        new("English", SubjectMaskNames.Languages, LanguageExpertise.English, "Languages"),
        new("World History", SubjectMaskNames.History, HistoryExpertise.WorldHistory, "History"),
        new("US History", SubjectMaskNames.History, HistoryExpertise.UsHistory, "History"),
        new("Marketing", SubjectMaskNames.Business, BusinessExpertise.Marketing, "Business"),
        new("Management", SubjectMaskNames.Business, BusinessExpertise.Management, "Business"),
        new("Drawing", SubjectMaskNames.Art, ArtExpertise.Drawing, "Art"),
        new("Painting", SubjectMaskNames.Art, ArtExpertise.Painting, "Art"),
        new("Music Theory", SubjectMaskNames.Music, MusicExpertise.MusicTheory, "Music"),
        new("Mechanical Engineering", SubjectMaskNames.Engineering, EngineeringExpertise.Mechanical, "Engineering"),
        new("Anatomy", SubjectMaskNames.Medicine, MedicineExpertise.Anatomy, "Medicine"),
        new("Investing", SubjectMaskNames.Finance, FinanceExpertise.Investing, "Finance"),
        new("Microeconomics", SubjectMaskNames.Economics, EconomicsExpertise.Microeconomics, "Economics"),
        new("Curriculum Design", SubjectMaskNames.Education, EducationExpertise.CurriculumDesign, "Education"),
    ];

    public static FrozenDictionary<string, RoleDefinition> RolesByName { get; } =
        Roles.ToFrozenDictionary(role => role.Name, StringComparer.Ordinal);

    public static FrozenDictionary<(string SubjectMask, short BitIndex), SubjectDefinition> SubjectsByKey { get; } =
        Subjects.ToFrozenDictionary(subject => (subject.SubjectMask, subject.BitIndex));

    public static FrozenDictionary<string, short[]> RolePermissionTiesByName { get; } =
        Roles.ToFrozenDictionary(role => role.Name, role => role.PermissionIds, StringComparer.Ordinal);

    public static IReadOnlyList<RolePermissionTie> RolePermissionTies { get; } =
        Roles.SelectMany(role =>
            role.PermissionIds.Select(permissionId => new RolePermissionTie(role.RoleId, permissionId)))
            .ToArray();

    public static int TotalRolePermissionTieCount => RolePermissionTies.Count;

    public static FrozenDictionary<string, RoleMaskBuilder.RoleMaskSet> PrecomputedRoleMasks { get; } =
        Roles.ToFrozenDictionary(
            role => role.Name,
            role => RoleMaskBuilder.Build(role.Name, role.PermissionIds),
            StringComparer.Ordinal);

    public static string ContentHashHex { get; } = ComputeContentHash();

    public static bool TryGetRole(string roleName, out RoleDefinition role) =>
        RolesByName.TryGetValue(roleName, out role!);

    public static bool TryGetSubject(string subjectMask, short bitIndex, out SubjectDefinition subject) =>
        SubjectsByKey.TryGetValue((subjectMask, bitIndex), out subject!);

    public static RoleMaskBuilder.RoleMaskSet GetRoleMasks(string roleName) =>
        PrecomputedRoleMasks[roleName];

    private static string ComputeContentHash()
    {
        StringBuilder builder = new();
        foreach (PermissionDefinition permission in Permissions)
            builder.Append('P').Append(permission.PermissionId).Append(':').Append(permission.Name);

        foreach (RoleDefinition role in Roles)
        {
            builder.Append('R').Append(role.Name);
            foreach (short permissionId in role.PermissionIds)
                builder.Append(',').Append(permissionId);
        }

        foreach (SubjectDefinition subject in Subjects)
            builder.Append('S')
                .Append(subject.SubjectMask).Append(':')
                .Append(subject.BitIndex).Append(':')
                .Append(subject.Name).Append(':')
                .Append(subject.ParentName);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash);
    }
}
