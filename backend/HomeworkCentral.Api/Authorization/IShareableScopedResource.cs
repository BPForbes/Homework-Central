namespace HomeworkCentral.Api.Authorization;

/// <summary>
/// Marks entities stored in shared community spaces (chat messages, future channels) where
/// visibility is split by real-vs-developer traffic only — not by tenant database. Chat messages
/// intentionally do not implement <see cref="IScopedResource"/> because tenant-scoping would
/// prevent dev personas in different tenant databases from sharing the same room history.
/// </summary>
public interface IShareableScopedResource
{
    AccountClass OwnerAccountClass { get; }
}
