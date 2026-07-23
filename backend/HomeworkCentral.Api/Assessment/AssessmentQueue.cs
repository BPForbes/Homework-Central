using System.Threading.Channels;

namespace HomeworkCentral.Api.Assessment;

public enum AssessmentJobKind
{
    /// <summary>Bounded ticket-evidence classification for a new message.</summary>
    Full = 0,

    /// <summary>Reserved for vote-driven recalculation; AI confidence currently ignores votes.</summary>
    CommunityRecalc = 1,
}

public sealed record AssessmentMessageJob(
    Guid MessageId,
    string RoomId,
    Guid SenderId,
    string Content,
    AssessmentJobKind Kind = AssessmentJobKind.Full);

public interface IAssessmentQueue
{
    bool TryEnqueue(AssessmentMessageJob job);
    IAsyncEnumerable<AssessmentMessageJob> ReadAllAsync(CancellationToken ct);
}

public sealed class AssessmentQueue(ILogger<AssessmentQueue> logger) : IAssessmentQueue
{
    private readonly Channel<AssessmentMessageJob> _channel =
        Channel.CreateBounded<AssessmentMessageJob>(new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

    public bool TryEnqueue(AssessmentMessageJob job)
    {
        bool accepted = _channel.Writer.TryWrite(job);
        if (!accepted)
            logger.LogWarning("Assessment queue is full; message {MessageId} remains unscored.", job.MessageId);
        return accepted;
    }

    public IAsyncEnumerable<AssessmentMessageJob> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
