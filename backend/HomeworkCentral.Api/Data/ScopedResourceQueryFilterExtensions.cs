using System.Reflection;
using HomeworkCentral.Api.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Data;

/// <summary>
/// Applies global query filters for every entity implementing <see cref="IScopedResource"/>.
/// Filters reference the scoped <see cref="AppDbContext"/> instance so
/// <see cref="IAccessScopeAccessor"/> can evaluate per request (not translatable to SQL).
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
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity =>
            context.AccessScopeAccessor == null
            || context.AccessScopeAccessor.CanQuery(entity.OwnerAccountClass, entity.TenantDatabaseName));
    }
}
