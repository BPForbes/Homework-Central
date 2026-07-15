namespace HomeworkCentral.Api.Chat.Mentions;

public enum SendMessageMentionError
{
    GuestCannotMention,
    MentionCooldown,
}

public sealed class SendMessageMentionException : Exception
{
    public SendMessageMentionError Error { get; }
    public TimeSpan? RetryAfter { get; }

    public SendMessageMentionException(SendMessageMentionError error, TimeSpan? retryAfter = null)
        : base(error.ToString())
    {
        Error = error;
        RetryAfter = retryAfter;
    }
}
