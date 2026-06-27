namespace HomeworkCentral.Api.Authorization;

/// <summary>
/// Platform role hierarchy. Child roles inherit parent role identity bits during
/// effective mask generation (e.g. HeadTutor implies SeniorTutor and Tutor).
/// </summary>
public static class RoleHierarchy
{
    private static readonly IReadOnlyDictionary<short, short[]> ParentRoleBits = new Dictionary<short, short[]>
    {
        [PlatformRoles.SeniorTutor] = [PlatformRoles.Tutor],
        [PlatformRoles.HeadTutor] = [PlatformRoles.SeniorTutor, PlatformRoles.Tutor],
        [PlatformRoles.SeniorModerator] = [PlatformRoles.Moderator],
        [PlatformRoles.SystemAdministrator] = [PlatformRoles.Administrator],
        [PlatformRoles.Owner] = [PlatformRoles.SystemAdministrator, PlatformRoles.Administrator],
    };

    public static IEnumerable<short> ExpandRoleBits(short roleBit)
    {
        yield return roleBit;

        if (!ParentRoleBits.TryGetValue(roleBit, out var parents))
            yield break;

        foreach (var parent in parents)
        {
            foreach (var expanded in ExpandRoleBits(parent))
                yield return expanded;
        }
    }
}
