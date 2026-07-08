using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace HomeworkCentral.Api.Chat;

public interface IChatNavNotifier
{
    Task NotifyNavChangedAsync(AccountClass accountClass, CancellationToken ct = default);
}

public sealed class ChatNavNotifier(IHubContext<ChatHub> hubContext) : IChatNavNotifier
{
    public Task NotifyNavChangedAsync(AccountClass accountClass, CancellationToken ct = default) =>
        hubContext.Clients
            .Group(ChatNavGroupKey.Build(accountClass))
            .SendAsync("ChatNavChanged", ct);
}
