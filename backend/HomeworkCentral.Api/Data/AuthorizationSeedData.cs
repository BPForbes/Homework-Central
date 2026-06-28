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
        var permissions = new (short Id, string Name, string Description)[]
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

        var existing = await db.Permissions.ToDictionaryAsync(p => p.PermissionId, ct);

        foreach (var (id, name, description) in permissions)
        {
            if (existing.TryGetValue(id, out var permission))
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

        var validIds = permissions.Select(p => p.Id).ToHashSet();
        var stale = await db.Permissions.Where(p => !validIds.Contains(p.PermissionId)).ToListAsync(ct);
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
        var roleDefinitions = new (string Name, string Description, short[] Permissions)[]
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

        var rolesByName = await db.Roles.ToDictionaryAsync(r => r.Name, ct);

        foreach (var (name, description, permissions) in roleDefinitions)
        {
            if (!rolesByName.TryGetValue(name, out var role))
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
            role.RolePermissions.Clear();

            foreach (var permissionId in permissions.Distinct())
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
        if (await db.Subjects.AnyAsync(ct))
            return;

        var science = AddSubject("Science", SubjectMaskNames.General, GeneralSubjects.Science);
        var computerScience = AddSubject("Computer Science", SubjectMaskNames.General, GeneralSubjects.ComputerScience);
        var mathematics = AddSubject("Mathematics", SubjectMaskNames.General, GeneralSubjects.Mathematics);
        var languages = AddSubject("Languages", SubjectMaskNames.General, GeneralSubjects.Languages);
        var history = AddSubject("History", SubjectMaskNames.General, GeneralSubjects.History);
        var business = AddSubject("Business", SubjectMaskNames.General, GeneralSubjects.Business);
        var art = AddSubject("Art", SubjectMaskNames.General, GeneralSubjects.Art);
        var music = AddSubject("Music", SubjectMaskNames.General, GeneralSubjects.Music);
        var engineering = AddSubject("Engineering", SubjectMaskNames.General, GeneralSubjects.Engineering);
        var medicine = AddSubject("Medicine", SubjectMaskNames.General, GeneralSubjects.Medicine);
        var finance = AddSubject("Finance", SubjectMaskNames.General, GeneralSubjects.Finance);
        var economics = AddSubject("Economics", SubjectMaskNames.General, GeneralSubjects.Economics);
        var education = AddSubject("Education", SubjectMaskNames.General, GeneralSubjects.Education);

        var biology = AddSubject("Biology", SubjectMaskNames.Science, ScienceExpertise.Biology, science.SubjectId);
        var chemistry = AddSubject("Chemistry", SubjectMaskNames.Science, ScienceExpertise.Chemistry, science.SubjectId);
        var physics = AddSubject("Physics", SubjectMaskNames.Science, ScienceExpertise.Physics, science.SubjectId);
        var philosophy = AddSubject("Philosophy", SubjectMaskNames.Science, ScienceExpertise.Philosophy, science.SubjectId);
        var psychology = AddSubject("Psychology", SubjectMaskNames.Science, ScienceExpertise.Psychology, science.SubjectId);

        var python = AddSubject("Python", SubjectMaskNames.ComputerScience, ComputerScienceExpertise.Python, computerScience.SubjectId);
        var csharp = AddSubject("C#", SubjectMaskNames.ComputerScience, ComputerScienceExpertise.CSharp, computerScience.SubjectId);
        var backend = AddSubject("Backend", SubjectMaskNames.ComputerScience, ComputerScienceExpertise.Backend, computerScience.SubjectId);
        var docker = AddSubject("Docker", SubjectMaskNames.ComputerScience, ComputerScienceExpertise.Docker, computerScience.SubjectId);

        var algebra = AddSubject("Algebra", SubjectMaskNames.Mathematics, MathematicsExpertise.Algebra, mathematics.SubjectId);
        var english = AddSubject("English", SubjectMaskNames.Languages, LanguageExpertise.English, languages.SubjectId);

        var worldHistory = AddSubject("World History", SubjectMaskNames.History, HistoryExpertise.WorldHistory, history.SubjectId);
        var usHistory = AddSubject("US History", SubjectMaskNames.History, HistoryExpertise.UsHistory, history.SubjectId);

        var marketing = AddSubject("Marketing", SubjectMaskNames.Business, BusinessExpertise.Marketing, business.SubjectId);
        var management = AddSubject("Management", SubjectMaskNames.Business, BusinessExpertise.Management, business.SubjectId);

        var drawing = AddSubject("Drawing", SubjectMaskNames.Art, ArtExpertise.Drawing, art.SubjectId);
        var painting = AddSubject("Painting", SubjectMaskNames.Art, ArtExpertise.Painting, art.SubjectId);

        var musicTheory = AddSubject("Music Theory", SubjectMaskNames.Music, MusicExpertise.MusicTheory, music.SubjectId);

        var mechanical = AddSubject("Mechanical Engineering", SubjectMaskNames.Engineering, EngineeringExpertise.Mechanical, engineering.SubjectId);

        var anatomy = AddSubject("Anatomy", SubjectMaskNames.Medicine, MedicineExpertise.Anatomy, medicine.SubjectId);

        var investing = AddSubject("Investing", SubjectMaskNames.Finance, FinanceExpertise.Investing, finance.SubjectId);

        var microeconomics = AddSubject("Microeconomics", SubjectMaskNames.Economics, EconomicsExpertise.Microeconomics, economics.SubjectId);

        var curriculumDesign = AddSubject("Curriculum Design", SubjectMaskNames.Education, EducationExpertise.CurriculumDesign, education.SubjectId);

        db.Subjects.AddRange(
            science, computerScience, mathematics, languages, history, business, art, music,
            engineering, medicine, finance, economics, education,
            biology, chemistry, physics, philosophy, psychology,
            python, csharp, backend, docker,
            algebra, english,
            worldHistory, usHistory,
            marketing, management,
            drawing, painting,
            musicTheory,
            mechanical,
            anatomy,
            investing,
            microeconomics,
            curriculumDesign);

        await db.SaveChangesAsync(ct);
    }

    private static Subject AddSubject(string name, string subjectMask, short bitIndex, Guid? parentSubjectId = null) =>
        new()
        {
            SubjectId = Guid.NewGuid(),
            Name = name,
            SubjectMask = subjectMask,
            BitIndex = bitIndex,
            ParentSubjectId = parentSubjectId,
        };
}
