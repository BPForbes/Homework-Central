using HomeworkCentral.Api.Infrastructure;

namespace HomeworkCentral.Api.Tests.Chat;

internal sealed class FixedCustomChannelStore(params CustomChannelSnapshot[] channels) : ICustomChannelStore
{
    private readonly IReadOnlyList<CustomChannelSnapshot> _channels = channels;

    public IReadOnlyList<CustomChannelSnapshot> Channels => _channels;

    public CustomChannelSnapshot? FindByRoomId(string roomId) =>
        _channels.FirstOrDefault(channel => string.Equals(channel.RoomId, roomId, StringComparison.Ordinal));

    public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
}
