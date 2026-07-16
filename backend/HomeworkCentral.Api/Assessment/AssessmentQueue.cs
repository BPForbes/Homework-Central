using System.Threading.Channels;

namespace HomeworkCentral.Api.Assessment;

public sealed record AssessmentMessageJob(
    Guid MessageId,
    string RoomId,
    Guid SenderId,
    string Content);

public interface IAssessmentQueue
{
    ValueTask EnqueueAsync(AssessmentMessageJob job, CancellationToken ct = default);
    IAsyncEnumerable<AssessmentMessageJob> ReadAllAsync(CancellationToken ct);
}

public sealed class AssessmentQueue : IAssessmentQueue
{
    private readonly Channel<AssessmentMessageJob> _channel =
        Channel.CreateUnbounded<AssessmentMessageJob>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    public ValueTask EnqueueAsync(AssessmentMessageJob job, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(job, ct);

    public IAsyncEnumerable<AssessmentMessageJob> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
