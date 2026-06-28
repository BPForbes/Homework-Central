using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Data;

public static class AuthorizationSeedData
{
    public static async Task SeedAsync(AppDbContext db, IRoleMaskService roleMaskService, CancellationToken ct = default)
    {
        await UpsertPermissionsAsync(db, ct);
        await UpsertRolesAsync(db, roleMaskService, ct);
        await UpsertSubjectsAsync(db, ct);
    }

    private static async Task UpsertPermissionsAsync(AppDbContext db, CancellationToken ct)
    {
        (short Id, string Name, string Description)[] permissions = new (short Id, string Name, string Description)[]
        {
            (ModerationPermissions.ViewReports, "ViewReports", "View moderation reports."),
            (ModerationPermissions.ResolveReports, "ResolveReports", "Resolve moderation reports."),
            (ModerationPermissions.WarnUser, "WarnUser", "Issue warnings to members."),
            (ModerationPermissions.TimeoutUser, "TimeoutUser", "Timeout members."),
            (ModerationPermissions.MuteMembers, "MuteMembers", "Mute and unmute members."),
            (ModerationPermissions.KickUser, "KickUser", "Kick members from sessions."),
            (ModerationPermissions.BanMembers, "BanMembers", "Ban and unban members."),
            (ModerationPermissions.DeleteMessages, "DeleteMessages", "Delete messages."),
            (ModerationPermissions.EditMessages, "EditMessages", "Edit messages."),
            (ModerationPermissions.PinMessages, "PinMessages", "Pin messages."),
            (ModerationPermissions.LockChannels, "LockChannels", "Lock and unlock channels."),
            (ModerationPermissions.ManageChannels, "ManageChannels", "Manage channels."),
            (ModerationPermissions.ManageRoles, "ManageRoles", "Grant and revoke platform roles."),
            (ModerationPermissions.ManagePermissions, "ManagePermissions", "Manage permission assignments."),
            (ModerationPermissions.ViewAuditLogs, "ViewAuditLogs", "View audit logs."),
            (ModerationPermissions.ManageEvents, "ManageEvents", "Create and manage community events."),
            (ModerationPermissions.ManageSeminars, "ManageSeminars", "Host and upload seminars."),
            (ModerationPermissions.ModerateResources, "ModerateResources", "Moderate shared resources."),
            (ModerationPermissions.SuspendAccounts, "SuspendAccounts", "Suspend and restore accounts."),
            (ModerationPermissions.HandleAppeals, "HandleAppeals", "Handle moderation appeals."),
        };

        Dictionary<short, Permission> existing = await db.Permissions.ToDictionaryAsync(p => p.PermissionId, ct);

        foreach ((short id, string name, string description) in permissions)
        {
            if (existing.TryGetValue(id, out Permission? permission))
            {
                permission.Name = name;
                permission.DisplayName = name;
                permission.Description = description;
                permission.Category = "Moderation";
            }
            else
            {
                db.Permissions.Add(new Permission
                {
                    PermissionId = id,
                    Name = name,
                    DisplayName = name,
                    Category = "Moderation",
                    Description = description,
                });
            }
        }

        HashSet<short> validIds = permissions.Select(p => p.Id).ToHashSet();
        List<Permission> stale = await db.Permissions
            .Where(p => p.Category == "Moderation" && !validIds.Contains(p.PermissionId))
            .ToListAsync(ct);
        if (stale.Count > 0)
        {
            db.RolePermissions.RemoveRange(
                await db.RolePermissions.Where(rp => stale.Select(s => s.PermissionId).Contains(rp.PermissionId)).ToListAsync(ct));
            db.Permissions.RemoveRange(stale);
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task UpsertRolesAsync(AppDbContext db, IRoleMaskService roleMaskService, CancellationToken ct)
    {
        (string Name, string Description, short[] Permissions)[] roleDefinitions = new (string Name, string Description, short[] Permissions)[]
        {
            ("Guest", "Unauthenticated or newly registered visitor.", []),
            ("VerifiedUser", "Verified community member.", []),
            ("Student", "Student participant.", []),
            ("Staff", "Platform staff member.", []),
            ("Tutor", "Homework tutor.", []),
            ("SeniorTutor", "Senior tutor with seminar and event responsibilities.",
            [
                ModerationPermissions.ManageSeminars,
                ModerationPermissions.ManageEvents,
            ]),
            ("HeadTutor", "Head tutor with appeals and role management responsibilities.",
            [
                ModerationPermissions.HandleAppeals,
                ModerationPermissions.ManageRoles,
                ModerationPermissions.ManageSeminars,
                ModerationPermissions.ManageEvents,
            ]),
            ("Moderator", "Community moderator.",
            [
                ModerationPermissions.ViewReports,
                ModerationPermissions.DeleteMessages,
                ModerationPermissions.MuteMembers,
            ]),
            ("SeniorModerator", "Senior community moderator.",
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
            ("CommunityManager", "Community manager.", []),
            ("EventOrganizer", "Event organizer.", [ModerationPermissions.ManageEvents]),
            ("SeminarHost", "Seminar host.", [ModerationPermissions.ManageSeminars]),
            ("VerifiedEducator", "Verified educator.", []),
            ("Developer", "Platform developer.", []),
            ("BetaTester", "Beta program tester.", []),
            ("Administrator", "Platform administrator.",
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
            ]),
            ("SystemAdministrator", "System administrator.",
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
            ]),
            ("BoardMember", "Board member.",
            [
                ModerationPermissions.ViewReports,
                ModerationPermissions.ViewAuditLogs,
                ModerationPermissions.HandleAppeals,
                ModerationPermissions.ManageRoles,
            ]),
            ("Owner", "Platform owner.", Enumerable.Range(0, ModerationPermissions.HandleAppeals + 1).Select(i => (short)i).ToArray()),
            ("Founder", "Platform founder.", Enumerable.Range(0, ModerationPermissions.HandleAppeals + 1).Select(i => (short)i).ToArray()),
        };

        Dictionary<string, Role> rolesByName = await db.Roles.ToDictionaryAsync(r => r.Name, ct);

        foreach ((string name, string description, short[] permissions) in roleDefinitions)
        {
            if (!rolesByName.TryGetValue(name, out Role? role))
            {
                role = new Role
                {
                    RoleId = Guid.NewGuid(),
                    Name = name,
                    Description = description,
                };
                db.Roles.Add(role);
                rolesByName[name] = role;
            }
            else
            {
                role.Description = description;
            }

            await db.Entry(role).Collection(r => r.RolePermissions).LoadAsync(ct);

            HashSet<short> desiredPermissionIds = permissions.Distinct().ToHashSet();
            List<RolePermission> toRemove = role.RolePermissions
                .Where(rp => !desiredPermissionIds.Contains(rp.PermissionId))
                .ToList();
            foreach (RolePermission rolePermission in toRemove)
                role.RolePermissions.Remove(rolePermission);

            HashSet<short> existingPermissionIds = role.RolePermissions
                .Select(rp => rp.PermissionId)
                .ToHashSet();

            foreach (short permissionId in desiredPermissionIds.Where(id => !existingPermissionIds.Contains(id)))
            {
                role.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.RoleId,
                    PermissionId = permissionId,
                });
            }
        }

        await db.SaveChangesAsync(ct);
        await roleMaskService.RebuildAllRoleMasksAsync(ct);
    }

    private static async Task UpsertSubjectsAsync(AppDbContext db, CancellationToken ct)
    {
        Dictionary<(string SubjectMask, short BitIndex), Subject> existing = await db.Subjects
            .ToDictionaryAsync(s => (s.SubjectMask, s.BitIndex), ct);

        Subject science = UpsertSubject(db, existing, "Science", SubjectMaskNames.General, GeneralSubjects.Science);
        Subject computerScience = UpsertSubject(db, existing, "Computer Science", SubjectMaskNames.General, GeneralSubjects.ComputerScience);
        Subject mathematics = UpsertSubject(db, existing, "Mathematics", SubjectMaskNames.General, GeneralSubjects.Mathematics);
        Subject languages = UpsertSubject(db, existing, "Languages", SubjectMaskNames.General, GeneralSubjects.Languages);
        Subject history = UpsertSubject(db, existing, "History", SubjectMaskNames.General, GeneralSubjects.History);
        Subject business = UpsertSubject(db, existing, "Business", SubjectMaskNames.General, GeneralSubjects.Business);
        Subject art = UpsertSubject(db, existing, "Art", SubjectMaskNames.General, GeneralSubjects.Art);
        Subject music = UpsertSubject(db, existing, "Music", SubjectMaskNames.General, GeneralSubjects.Music);
        Subject engineering = UpsertSubject(db, existing, "Engineering", SubjectMaskNames.General, GeneralSubjects.Engineering);
        Subject medicine = UpsertSubject(db, existing, "Medicine", SubjectMaskNames.General, GeneralSubjects.Medicine);
        Subject finance = UpsertSubject(db, existing, "Finance", SubjectMaskNames.General, GeneralSubjects.Finance);
        Subject economics = UpsertSubject(db, existing, "Economics", SubjectMaskNames.General, GeneralSubjects.Economics);
        Subject education = UpsertSubject(db, existing, "Education", SubjectMaskNames.General, GeneralSubjects.Education);

        UpsertSubject(db, existing, "Biology", SubjectMaskNames.Science, ScienceExpertise.Biology, science.SubjectId);
        UpsertSubject(db, existing, "Chemistry", SubjectMaskNames.Science, ScienceExpertise.Chemistry, science.SubjectId);
        UpsertSubject(db, existing, "Physics", SubjectMaskNames.Science, ScienceExpertise.Physics, science.SubjectId);
        UpsertSubject(db, existing, "Philosophy", SubjectMaskNames.Science, ScienceExpertise.Philosophy, science.SubjectId);
        UpsertSubject(db, existing, "Psychology", SubjectMaskNames.Science, ScienceExpertise.Psychology, science.SubjectId);

        UpsertSubject(db, existing, "Python", SubjectMaskNames.ComputerScience, ComputerScienceExpertise.Python, computerScience.SubjectId);
        UpsertSubject(db, existing, "C#", SubjectMaskNames.ComputerScience, ComputerScienceExpertise.CSharp, computerScience.SubjectId);
        UpsertSubject(db, existing, "Backend", SubjectMaskNames.ComputerScience, ComputerScienceExpertise.Backend, computerScience.SubjectId);
        UpsertSubject(db, existing, "Docker", SubjectMaskNames.ComputerScience, ComputerScienceExpertise.Docker, computerScience.SubjectId);

        UpsertSubject(db, existing, "Algebra", SubjectMaskNames.Mathematics, MathematicsExpertise.Algebra, mathematics.SubjectId);
        UpsertSubject(db, existing, "English", SubjectMaskNames.Languages, LanguageExpertise.English, languages.SubjectId);

        UpsertSubject(db, existing, "World History", SubjectMaskNames.History, HistoryExpertise.WorldHistory, history.SubjectId);
        UpsertSubject(db, existing, "US History", SubjectMaskNames.History, HistoryExpertise.UsHistory, history.SubjectId);

        UpsertSubject(db, existing, "Marketing", SubjectMaskNames.Business, BusinessExpertise.Marketing, business.SubjectId);
        UpsertSubject(db, existing, "Management", SubjectMaskNames.Business, BusinessExpertise.Management, business.SubjectId);

        UpsertSubject(db, existing, "Drawing", SubjectMaskNames.Art, ArtExpertise.Drawing, art.SubjectId);
        UpsertSubject(db, existing, "Painting", SubjectMaskNames.Art, ArtExpertise.Painting, art.SubjectId);

        UpsertSubject(db, existing, "Music Theory", SubjectMaskNames.Music, MusicExpertise.MusicTheory, music.SubjectId);

        UpsertSubject(db, existing, "Mechanical Engineering", SubjectMaskNames.Engineering, EngineeringExpertise.Mechanical, engineering.SubjectId);

        UpsertSubject(db, existing, "Anatomy", SubjectMaskNames.Medicine, MedicineExpertise.Anatomy, medicine.SubjectId);

        UpsertSubject(db, existing, "Investing", SubjectMaskNames.Finance, FinanceExpertise.Investing, finance.SubjectId);

        UpsertSubject(db, existing, "Microeconomics", SubjectMaskNames.Economics, EconomicsExpertise.Microeconomics, economics.SubjectId);

        UpsertSubject(db, existing, "Curriculum Design", SubjectMaskNames.Education, EducationExpertise.CurriculumDesign, education.SubjectId);

        await db.SaveChangesAsync(ct);
    }

    private static Subject UpsertSubject(
        AppDbContext db,
        Dictionary<(string SubjectMask, short BitIndex), Subject> existing,
        string name,
        string subjectMask,
        short bitIndex,
        Guid? parentSubjectId = null)
    {
        (string SubjectMask, short BitIndex) key = (subjectMask, bitIndex);
        if (existing.TryGetValue(key, out Subject? subject))
        {
            subject.Name = name;
            subject.ParentSubjectId = parentSubjectId;
            return subject;
        }

        subject = new Subject
        {
            SubjectId = Guid.NewGuid(),
            Name = name,
            SubjectMask = subjectMask,
            BitIndex = bitIndex,
            ParentSubjectId = parentSubjectId,
        };
        db.Subjects.Add(subject);
        existing[key] = subject;
        return subject;
    }
}
