namespace HomeworkCentral.Api.Authorization;

/// <summary>
/// Captures how EF global query filters should behave for a single <see cref="Data.AppDbContext"/>
/// instance: unrestricted (no HTTP request), denied (request without a valid scope), or scoped.
/// </summary>
public sealed class DbContextAccessScope
{
    public bool BypassFilters { get; init; }

    public AccessScope? Scope { get; init; }

    public bool IsAuthenticated => Scope is not null;

    public AccountClass AccountClass => Scope?.AccountClass ?? default;

    public string? TenantDatabaseName => Scope?.TenantDatabaseName;

    public static DbContextAccessScope Unrestricted() => new() { BypassFilters = true };

    public static DbContextAccessScope Denied() => new();

    public static DbContextAccessScope Scoped(AccessScope scope) => new() { Scope = scope };
}
