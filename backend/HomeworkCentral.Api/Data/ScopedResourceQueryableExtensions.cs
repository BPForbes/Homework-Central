using HomeworkCentral.Api.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Data;

/// <summary>
/// SQL-translatable scoping for <see cref="IScopedResource"/> entities. Use instead of the
/// global EF filter when <see cref="IAccessScopeAccessor.CanQuery"/> cannot be translated.
/// </summary>
public static class ScopedResourceQueryableExtensions
{
    public static IQueryable<T> ForCurrentViewer<T>(this IQueryable<T> query, AccessScope scope)
        where T : class, IScopedResource =>
        ApplyScope(query.IgnoreQueryFilters(), scope);

    internal static IQueryable<T> ApplyScope<T>(IQueryable<T> query, AccessScope scope)
        where T : class, IScopedResource
    {
        return scope.AccountClass switch
        {
            AccountClass.RealAccount => query.Where(entity =>
                entity.OwnerAccountClass == AccountClass.RealAccount
                && entity.TenantDatabaseName == scope.TenantDatabaseName),
            AccountClass.DeveloperAccount => query.Where(entity =>
                entity.OwnerAccountClass == AccountClass.DeveloperAccount
                && entity.TenantDatabaseName == scope.TenantDatabaseName),
            AccountClass.DevAdmin => query.Where(entity =>
                entity.OwnerAccountClass != AccountClass.RealAccount),
            _ => query.Where(_ => false),
        };
    }
}
