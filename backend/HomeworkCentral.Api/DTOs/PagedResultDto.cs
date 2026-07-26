namespace HomeworkCentral.Api.DTOs;

/// <summary>
/// Cursor page for large list endpoints. Clients pass <see cref="NextBeforeUtc"/> back as
/// <c>beforeUtc</c> to fetch the next older page.
/// </summary>
public sealed record PagedResultDto<T>(
    IReadOnlyList<T> Items,
    bool HasMore,
    DateTime? NextBeforeUtc,
    int Limit);
