using System.Collections.Frozen;

namespace HomeworkCentral.Api.Authorization;

/// <summary>Maps platform role names to authority levels (bit index) for grant checks.</summary>
public static class PlatformRoleCatalog
{
    private static readonly FrozenDictionary<string, short> RoleBits =
        new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase)
        {
            ["Guest"] = PlatformRoles.Guest,
            ["VerifiedUser"] = PlatformRoles.VerifiedUser,
            ["Student"] = PlatformRoles.Student,
            ["Staff"] = PlatformRoles.Staff,
            ["Tutor"] = PlatformRoles.Tutor,
            ["TrialTutor"] = PlatformRoles.TrialTutor,
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
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> CanonicalRoleNames =
        RoleBits.Keys.ToFrozenDictionary(k => k, k => k, StringComparer.OrdinalIgnoreCase);

    public static bool TryGetRoleBit(string roleName, out short bit) =>
        RoleBits.TryGetValue(roleName, out bit);

    public static bool TryGetRoleNameFromBit(short bit, out string roleName)
    {
        string? match = RoleBits
            .Where(entry => entry.Value == bit)
            .Select(entry => entry.Key)
            .FirstOrDefault();
        if (match is null)
        {
            roleName = string.Empty;
            return false;
        }

        roleName = match;
        return true;
    }

    public static bool TryGetCanonicalRoleName(string roleName, out string canonicalName, out short bit)
    {
        if (!RoleBits.TryGetValue(roleName, out bit))
        {
            canonicalName = string.Empty;
            return false;
        }

        canonicalName = CanonicalRoleNames[roleName];
        return true;
    }

    public static short GetHighestRoleBit(IEnumerable<string> roleNames) =>
        roleNames.Aggregate(PlatformRoles.Guest, static (highest, roleName) =>
        {
            if (!TryGetRoleBit(roleName, out short bit) || bit == PlatformRoles.TrialTutor)
                return highest;
            return bit > highest ? bit : highest;
        });

    public static short GetHighestRoleBit(System.Collections.BitArray roleMask) =>
        Enumerable.Range(PlatformRoles.Guest, PlatformRoles.Founder - PlatformRoles.Guest + 1)
            .Select(static bit => (short)bit)
            .Where(bit => bit < roleMask.Length && roleMask[bit])
            .DefaultIfEmpty(PlatformRoles.Guest)
            .Max();

    /// <summary>
    /// Returns true when <paramref name="targetRoleBit"/> is strictly below <paramref name="granterRoleBit"/>.
    /// TrialTutor is treated as Tutor-level for grant checks so it remains grantable by HeadTutor+.
    /// </summary>
    public static bool CanGrantRole(short granterRoleBit, short targetRoleBit)
    {
        short effectiveTarget = targetRoleBit == PlatformRoles.TrialTutor
            ? PlatformRoles.Tutor
            : targetRoleBit;
        return effectiveTarget < granterRoleBit;
    }
}
