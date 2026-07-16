using System.Threading.Channels;

namespace HomeworkCentral.Api.Assessment;

public enum AssessmentJobKind
{
    /// <summary>Full eligibility + LLM rubric + community blend for a new message.</summary>
    Full = 0,

    /// <summary>Re-aggregate community votes and adjust combined scores without re-calling the LLM.</summary>
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
