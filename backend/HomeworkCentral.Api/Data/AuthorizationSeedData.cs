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
        await SeedPermissionsAsync(db, ct);
        await SeedRolesAsync(db, roleMaskService, ct);
        await SeedSubjectsAsync(db, ct);
    }

    private static async Task SeedPermissionsAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Permissions.AnyAsync(ct))
            return;

        var permissions = new (short Id, string Name, string Category)[]
        {
            (ModerationPermissions.ViewReports, "ViewReports", "Moderation"),
            (ModerationPermissions.ResolveReports, "ResolveReports", "Moderation"),
            (ModerationPermissions.WarnUser, "WarnUser", "Moderation"),
            (ModerationPermissions.TimeoutUser, "TimeoutUser", "Moderation"),
            (ModerationPermissions.MuteUser, "MuteUser", "Moderation"),
            (ModerationPermissions.UnmuteUser, "UnmuteUser", "Moderation"),
            (ModerationPermissions.KickUser, "KickUser", "Moderation"),
            (ModerationPermissions.BanUser, "BanUser", "Moderation"),
            (ModerationPermissions.UnbanUser, "UnbanUser", "Moderation"),
            (ModerationPermissions.DeleteMessages, "DeleteMessages", "Moderation"),
            (ModerationPermissions.EditMessages, "EditMessages", "Moderation"),
            (ModerationPermissions.PinMessages, "PinMessages", "Moderation"),
            (ModerationPermissions.LockChannel, "LockChannel", "Moderation"),
            (ModerationPermissions.UnlockChannel, "UnlockChannel", "Moderation"),
            (ModerationPermissions.ManageChannels, "ManageChannels", "Moderation"),
            (ModerationPermissions.ManageRoles, "ManageRoles", "Moderation"),
            (ModerationPermissions.ManagePermissions, "ManagePermissions", "Moderation"),
            (ModerationPermissions.ViewAuditLogs, "ViewAuditLogs", "Moderation"),
            (ModerationPermissions.ManageEvents, "ManageEvents", "Moderation"),
            (ModerationPermissions.ManageSeminars, "ManageSeminars", "Moderation"),
            (ModerationPermissions.ModerateResources, "ModerateResources", "Moderation"),
            (ModerationPermissions.SuspendAccount, "SuspendAccount", "Moderation"),
            (ModerationPermissions.RestoreAccount, "RestoreAccount", "Moderation"),
            (ModerationPermissions.HandleAppeals, "HandleAppeals", "Moderation"),
        };

        foreach (var (id, name, category) in permissions)
        {
            db.Permissions.Add(new Permission
            {
                PermissionId = id,
                Name = name,
                DisplayName = name,
                Category = category,
                Description = $"{name} moderation permission.",
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedRolesAsync(AppDbContext db, IRoleMaskService roleMaskService, CancellationToken ct)
    {
        if (await db.Roles.AnyAsync(ct))
            return;

        var guest = AddRole("Guest", "Unauthenticated or newly registered visitor.");
        var verifiedUser = AddRole("VerifiedUser", "Verified community member.");
        var student = AddRole("Student", "Student participant.");
        var tutor = AddRole("Tutor", "Homework tutor.");
        var seniorTutor = AddRole("SeniorTutor", "Senior homework tutor.");
        var headTutor = AddRole("HeadTutor", "Head homework tutor.");
        var moderator = AddRole("Moderator", "Community moderator.");
        var seniorModerator = AddRole("SeniorModerator", "Senior community moderator.");
        var seminarHost = AddRole("SeminarHost", "Seminar host.");
        var administrator = AddRole("Administrator", "Platform administrator.");
        var owner = AddRole("Owner", "Platform owner.");

        db.Roles.AddRange(
            guest, verifiedUser, student, tutor, seniorTutor, headTutor,
            moderator, seniorModerator, seminarHost, administrator, owner);

        await db.SaveChangesAsync(ct);

        AddModeratorPermissions(db, moderator.RoleId, seniorModerator.RoleId, administrator.RoleId, owner.RoleId);
        await db.SaveChangesAsync(ct);

        await roleMaskService.RebuildAllRoleMasksAsync(ct);
    }

    private static Role AddRole(string name, string description) =>
        new()
        {
            RoleId = Guid.NewGuid(),
            Name = name,
            Description = description,
        };

    private static void AddModeratorPermissions(
        AppDbContext db,
        Guid moderatorId,
        Guid seniorModeratorId,
        Guid administratorId,
        Guid ownerId)
    {
        var baseModeration = new short[]
        {
            ModerationPermissions.ViewReports,
            ModerationPermissions.ResolveReports,
            ModerationPermissions.WarnUser,
            ModerationPermissions.TimeoutUser,
            ModerationPermissions.MuteUser,
            ModerationPermissions.UnmuteUser,
            ModerationPermissions.DeleteMessages,
            ModerationPermissions.PinMessages,
            ModerationPermissions.ModerateResources,
        };

        foreach (var permissionId in baseModeration)
            db.RolePermissions.Add(new RolePermission { RoleId = moderatorId, PermissionId = permissionId });

        var seniorModeration = baseModeration.Concat([
            ModerationPermissions.KickUser,
            ModerationPermissions.LockChannel,
            ModerationPermissions.UnlockChannel,
            ModerationPermissions.HandleAppeals,
        ]);

        foreach (var permissionId in seniorModeration)
            db.RolePermissions.Add(new RolePermission { RoleId = seniorModeratorId, PermissionId = permissionId });

        for (short permissionId = 0; permissionId <= ModerationPermissions.HandleAppeals; permissionId++)
        {
            db.RolePermissions.Add(new RolePermission { RoleId = administratorId, PermissionId = permissionId });
            db.RolePermissions.Add(new RolePermission { RoleId = ownerId, PermissionId = permissionId });
        }
    }

    private static async Task SeedSubjectsAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Subjects.AnyAsync(ct))
            return;

        var science = AddSubject("Science", SubjectMaskNames.General, GeneralSubjects.Science);
        var computerScience = AddSubject("Computer Science", SubjectMaskNames.General, GeneralSubjects.ComputerScience);
        var mathematics = AddSubject("Mathematics", SubjectMaskNames.General, GeneralSubjects.Mathematics);
        var languages = AddSubject("Languages", SubjectMaskNames.General, GeneralSubjects.Languages);

        var python = AddSubject("Python", SubjectMaskNames.ComputerScience, ComputerScienceExpertise.Python, computerScience.SubjectId);
        var csharp = AddSubject("C#", SubjectMaskNames.ComputerScience, ComputerScienceExpertise.CSharp, computerScience.SubjectId);
        var backend = AddSubject("Backend", SubjectMaskNames.ComputerScience, ComputerScienceExpertise.Backend, computerScience.SubjectId);
        var docker = AddSubject("Docker", SubjectMaskNames.ComputerScience, ComputerScienceExpertise.Docker, computerScience.SubjectId);

        var algebra = AddSubject("Algebra", SubjectMaskNames.Mathematics, MathematicsExpertise.Algebra, mathematics.SubjectId);
        var english = AddSubject("English", SubjectMaskNames.Languages, LanguageExpertise.English, languages.SubjectId);

        db.Subjects.AddRange(
            science, computerScience, mathematics, languages,
            python, csharp, backend, docker,
            algebra, english);

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
