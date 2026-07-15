using HomeworkCentral.Api.Infrastructure;

namespace HomeworkCentral.Api.Tests.Chat;

internal sealed class EmptyCustomChannelStore : ICustomChannelStore
{
    public IReadOnlyList<CustomChannelSnapshot> Channels => [];

    public CustomChannelSnapshot? FindByRoomId(string roomId) => null;

    public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
}
