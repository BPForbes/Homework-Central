namespace HomeworkCentral.Api.Authorization;

/// <summary>Maps platform role names to authority levels (bit index) for grant checks.</summary>
public static class PlatformRoleCatalog
{
    private static readonly IReadOnlyDictionary<string, short> RoleBits =
        new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase)
        {
            ["Guest"] = PlatformRoles.Guest,
            ["VerifiedUser"] = PlatformRoles.VerifiedUser,
            ["Student"] = PlatformRoles.Student,
            ["Staff"] = PlatformRoles.Staff,
            ["Tutor"] = PlatformRoles.Tutor,
            ["SeniorTutor"] = PlatformRoles.SeniorTutor,
            ["HeadTutor"] = PlatformRoles.HeadTutor,
            ["Moderator"] = PlatformRoles.Moderator,
            ["SeniorModerator"] = PlatformRoles.SeniorModerator,
            ["CommunityManager"] = PlatformRoles.CommunityManager,
            ["EventOrganizer"] = PlatformRoles.EventOrganizer,
            ["SeminarHost"] = PlatformRoles.SeminarHost,
            ["VerifiedEducator"] = PlatformRoles.VerifiedEducator,
            ["Developer"] = PlatformRoles.Developer,
            ["BetaTester"] = PlatformRoles.BetaTester,
            ["Administrator"] = PlatformRoles.Administrator,
            ["SystemAdministrator"] = PlatformRoles.SystemAdministrator,
            ["BoardMember"] = PlatformRoles.BoardMember,
            ["Owner"] = PlatformRoles.Owner,
            ["Founder"] = PlatformRoles.Founder,
        };

    public static bool TryGetRoleBit(string roleName, out short bit) =>
        RoleBits.TryGetValue(roleName, out bit);

    public static bool TryGetCanonicalRoleName(string roleName, out string canonicalName, out short bit)
    {
        foreach (KeyValuePair<string, short> entry in RoleBits)
        {
            if (!string.Equals(entry.Key, roleName, StringComparison.OrdinalIgnoreCase))
                continue;

            canonicalName = entry.Key;
            bit = entry.Value;
            return true;
        }

        canonicalName = string.Empty;
        bit = 0;
        return false;
    }

    public static short GetHighestRoleBit(IEnumerable<string> roleNames)
    {
        short highest = PlatformRoles.Guest;
        foreach (string roleName in roleNames)
        {
            if (TryGetRoleBit(roleName, out short bit) && bit > highest)
                highest = bit;
        }

        return highest;
    }

    public static short GetHighestRoleBit(System.Collections.BitArray roleMask)
    {
        short highest = PlatformRoles.Guest;
        for (short bit = PlatformRoles.Guest; bit <= PlatformRoles.Founder; bit++)
        {
            if (bit < roleMask.Length && roleMask[bit] && bit > highest)
                highest = bit;
        }

        return highest;
    }

    /// <summary>Returns true when <paramref name="targetRoleBit"/> is strictly below <paramref name="granterRoleBit"/>.</summary>
    public static bool CanGrantRole(short granterRoleBit, short targetRoleBit) =>
        targetRoleBit < granterRoleBit;
}
