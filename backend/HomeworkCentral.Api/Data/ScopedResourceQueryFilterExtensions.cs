using System.Reflection;
using HomeworkCentral.Api.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Data;

/// <summary>
/// Applies global query filters for entities implementing <see cref="IScopedResource"/> or
/// <see cref="IShareableScopedResource"/>. <see cref="IScopedResource"/> filters enforce the
/// same rules as <see cref="ResourceVisibilityScope.CanView"/>. The filter
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
        MethodInfo? setScopedFilter = typeof(ScopedResourceQueryFilterExtensions)
            .GetMethod(nameof(SetScopedResourceFilter), BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo? setShareableFilter = typeof(ScopedResourceQueryFilterExtensions)
            .GetMethod(nameof(SetShareableScopedResourceFilter), BindingFlags.NonPublic | BindingFlags.Static);

        if (setScopedFilter is null || setShareableFilter is null)
            return;

        foreach (Microsoft.EntityFrameworkCore.Metadata.IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            Type clrType = entityType.ClrType;
            if (typeof(IScopedResource).IsAssignableFrom(clrType))
                setScopedFilter.MakeGenericMethod(clrType).Invoke(null, [modelBuilder, context]);
            else if (typeof(IShareableScopedResource).IsAssignableFrom(clrType))
                setShareableFilter.MakeGenericMethod(clrType).Invoke(null, [modelBuilder, context]);
        }
    }

    private static void SetShareableScopedResourceFilter<TEntity>(ModelBuilder modelBuilder, AppDbContext context)
        where TEntity : class, IShareableScopedResource
    {
        // Real production accounts see only RealAccount traffic; all other account classes share
        // developer/test chat history regardless of tenant database (see ChatMessage.cs).
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity =>
            context.ScopeBypassFilters
            || (context.ScopeIsAuthenticated
                && ShareableResourceVisibilityScope.CanView(
                    context.ScopeAccountClass,
                    entity.OwnerAccountClass)));
    }

    private static void SetScopedResourceFilter<TEntity>(ModelBuilder modelBuilder, AppDbContext context)
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
