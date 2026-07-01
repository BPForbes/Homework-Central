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

    // Comparing outputs only against each other (as the tests above do) can't catch a change to
    // the hashing scheme, namespace, or byte order that happens to still be internally
    // consistent (e.g. stable and case-insensitive, but different from before) — that would
    // silently reassign every existing seeded row's ID on next deploy. Pinning one literal per
    // kind turns that into a loud test failure instead. If a change is intentional, recompute and
    // update these deliberately (and see AuthorizationSeedData's fail-fast ID-mismatch checks,
    // which are the runtime backstop for the same class of bug).
    [Fact]
    public void Role_matches_pinned_known_value_for_Owner()
    {
        Assert.Equal(Guid.Parse("81290a0a-259a-50e6-8d73-e60d370fc700"), AuthorizationGuids.Role("Owner"));
    }

    [Fact]
    public void Subject_matches_pinned_known_value_for_science_biology()
    {
        Assert.Equal(
            Guid.Parse("cfcc8191-ab7b-5a9e-b92b-d6f9f372f33f"),
            AuthorizationGuids.Subject(SubjectMaskNames.Science, ScienceExpertise.Biology));
    }

    [Fact]
    public void DevUser_matches_pinned_known_value_for_doc_brown()
    {
        Assert.Equal(
            Guid.Parse("019a3044-f234-557e-8ead-da38cdd8151a"),
            AuthorizationGuids.DevUser("doc.brown@science.dev"));
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

    // Pinned to the catalog's current content — repeatability alone (comparing the hash to
    // itself) can't detect catalog drift, since a broken hash function could still be "stable"
    // (e.g. always returning a fixed string). If a legitimate catalog change causes this to
    // fail, recompute and update the pinned literal deliberately rather than loosening the test.
    private const string ExpectedContentHashHex =
        "A9904995010DABE523C1C36F92F7EA1F506EF4497529CB3C9AF7B20B02F01408";

    [Fact]
    public void ContentHashHex_is_stable()
    {
        string first = AuthorizationCatalog.ContentHashHex;
        string second = AuthorizationCatalog.ContentHashHex;
        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.Equal(ExpectedContentHashHex, first);
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
