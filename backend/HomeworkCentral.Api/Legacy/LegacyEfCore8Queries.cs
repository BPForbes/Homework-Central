using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Legacy;

/// <summary>Transitional EF Core 8 query helpers. Do not use in new code.</summary>
public static class LegacyEfCore8Queries
{
    [Obsolete("EF Core 10: use IQueryable.ToHashSetAsync() instead of ToListAsync() followed by ToHashSet().")]
    public static async Task<HashSet<TSource>> ToHashSetViaListAsync<TSource>(
        this IQueryable<TSource> query,
        CancellationToken cancellationToken = default)
    {
        List<TSource> items = await query.ToListAsync(cancellationToken);
        return items.ToHashSet();
    }
}
