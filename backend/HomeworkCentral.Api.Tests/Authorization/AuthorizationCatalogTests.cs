using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Data;

namespace HomeworkCentral.Api.Tests.Authorization;

public class AuthorizationGuidsTests
{
    [Fact]
    public void Role_returns_stable_guid_for_same_name()
    {
        Guid first = AuthorizationGuids.Role("Owner");
        Guid second = AuthorizationGuids.Role("Owner");
        Assert.Equal(first, second);
    }

    [Fact]
    public void Role_returns_distinct_guids_for_different_names()
    {
        Guid owner = AuthorizationGuids.Role("Owner");
        Guid tutor = AuthorizationGuids.Role("Tutor");
        Assert.NotEqual(owner, tutor);
    }

    [Fact]
    public void Subject_returns_stable_guid_for_same_mask_and_bit()
    {
        Guid first = AuthorizationGuids.Subject(SubjectMaskNames.Science, ScienceExpertise.Biology);
        Guid second = AuthorizationGuids.Subject(SubjectMaskNames.Science, ScienceExpertise.Biology);
        Assert.Equal(first, second);
    }

    [Fact]
    public void DevUser_is_case_insensitive()
    {
        Guid lower = AuthorizationGuids.DevUser("doc.brown@science.dev");
        Guid upper = AuthorizationGuids.DevUser("Doc.Brown@Science.Dev");
        Assert.Equal(lower, upper);
    }
}

public class AuthorizationCatalogTests
{
    [Fact]
    public void RolePermissionTies_match_role_permission_definitions()
    {
        int expected = AuthorizationCatalog.Roles.Sum(role => role.PermissionIds.Length);
        Assert.Equal(expected, AuthorizationCatalog.TotalRolePermissionTieCount);
        Assert.Equal(expected, AuthorizationCatalog.RolePermissionTies.Count);
    }

    [Fact]
    public void PrecomputedRoleMasks_include_owner_permissions()
    {
        RoleMaskBuilder.RoleMaskSet ownerMasks = AuthorizationCatalog.GetRoleMasks("Owner");
        Assert.True(BitMaskHasBit(ownerMasks.PermissionMask, ModerationPermissions.ViewReports));
        Assert.True(BitMaskHasBit(ownerMasks.PermissionMask, ModerationPermissions.HandleAppeals));
    }

    [Fact]
    public void ContentHashHex_is_stable()
    {
        string first = AuthorizationCatalog.ContentHashHex;
        string second = AuthorizationCatalog.ContentHashHex;
        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public void Roles_use_deterministic_role_ids()
    {
        AuthorizationCatalog.RoleDefinition owner = AuthorizationCatalog.RolesByName["Owner"];
        Assert.Equal(AuthorizationGuids.Role("Owner"), owner.RoleId);
    }

    private static bool BitMaskHasBit(System.Collections.BitArray mask, short bit) =>
        bit >= 0 && bit < mask.Length && mask[bit];
}
