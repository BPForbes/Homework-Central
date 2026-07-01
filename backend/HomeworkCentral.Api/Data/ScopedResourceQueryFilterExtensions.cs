using System.Reflection;
using HomeworkCentral.Api.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Data;

/// <summary>
/// Applies global query filters for every entity implementing <see cref="IScopedResource"/>,
/// enforcing the same rules as <see cref="ResourceVisibilityScope.CanView"/>. The filter
/// predicate is built entirely from <see cref="AppDbContext"/>'s own scalar properties
/// (<see cref="AppDbContext.ScopeIsAuthenticated"/> etc., resolved once per DbContext instance)
/// and the entity's own columns — it deliberately never calls back into
/// <see cref="IAccessScopeAccessor"/>'s methods, because EF Core cannot translate an arbitrary
/// C# method call inside a query filter to SQL. Calling <c>IAccessScopeAccessor.CanQuery(...)</c>
/// directly here compiles fine but throws at query time ("could not be translated") the moment
/// any <see cref="IScopedResource"/> entity is actually queried.
/// </summary>
public static class ScopedResourceQueryFilterExtensions
{
    public static void ApplyScopedResourceFilters(this ModelBuilder modelBuilder, AppDbContext context)
    {
        MethodInfo? setFilter = typeof(ScopedResourceQueryFilterExtensions)
            .GetMethod(nameof(SetFilter), BindingFlags.NonPublic | BindingFlags.Static);

        if (setFilter is null)
            return;

        foreach (Microsoft.EntityFrameworkCore.Metadata.IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            Type clrType = entityType.ClrType;
            if (!typeof(IScopedResource).IsAssignableFrom(clrType))
                continue;

            setFilter.MakeGenericMethod(clrType).Invoke(null, [modelBuilder, context]);
        }
    }

    private static void SetFilter<TEntity>(ModelBuilder modelBuilder, AppDbContext context)
        where TEntity : class, IScopedResource
    {
        // Mirrors ResourceVisibilityScope.CanView's switch, but expressed as a flat boolean so
        // every operand is either a DbContext scalar, an entity column, or a constant — all
        // translatable. ScopeBypassFilters is true only for non-request contexts (migrations, seed
        // jobs with no IHttpContextAccessor). HTTP requests without a valid scope deny all rows.
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity =>
            context.ScopeBypassFilters
            || (context.ScopeIsAuthenticated
                && ((context.ScopeAccountClass == AccountClass.RealAccount
                        && entity.OwnerAccountClass == AccountClass.RealAccount
                        && entity.TenantDatabaseName == context.ScopeTenantDatabaseName)
                    || (context.ScopeAccountClass == AccountClass.DevAdmin
                        && entity.OwnerAccountClass != AccountClass.RealAccount)
                    || (context.ScopeAccountClass == AccountClass.DeveloperAccount
                        && entity.OwnerAccountClass == AccountClass.DeveloperAccount
                        && entity.TenantDatabaseName == context.ScopeTenantDatabaseName))));
    }
}
