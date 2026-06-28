namespace HomeworkCentral.Api.Authorization;

/// <summary>
/// Platform role hierarchy. Child roles inherit parent role identity bits during
/// effective mask generation (e.g. HeadTutor implies SeniorTutor and Tutor).
/// </summary>
public static class RoleHierarchy
{
    private static readonly IReadOnlyDictionary<short, short[]> ParentRoleBits = new Dictionary<short, short[]>
    {
        [PlatformRoles.Staff] = [PlatformRoles.Student],
        [PlatformRoles.Tutor] = [PlatformRoles.Staff],
        [PlatformRoles.SeniorTutor] = [PlatformRoles.Tutor, PlatformRoles.EventOrganizer, PlatformRoles.SeminarHost],
        [PlatformRoles.HeadTutor] = [PlatformRoles.SeniorTutor],
        [PlatformRoles.SeniorModerator] = [PlatformRoles.Moderator],
        [PlatformRoles.SystemAdministrator] = [PlatformRoles.Administrator],
        [PlatformRoles.BoardMember] = [PlatformRoles.SystemAdministrator, PlatformRoles.Administrator],
        [PlatformRoles.Owner] = [PlatformRoles.BoardMember],
        [PlatformRoles.Founder] = [PlatformRoles.Owner],
    };

    public static IEnumerable<short> ExpandRoleBits(short roleBit)
    {
        yield return roleBit;

        if (!ParentRoleBits.TryGetValue(roleBit, out short[]? parents))
            yield break;

        foreach (short parent in parents)
        {
            foreach (short expanded in ExpandRoleBits(parent))
                yield return expanded;
        }
    }
}
