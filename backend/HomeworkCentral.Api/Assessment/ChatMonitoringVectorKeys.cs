namespace HomeworkCentral.Api.Assessment;

/// <summary>Stable vector <c>PositionId</c> values shared by training upserts and live reviewer retrieval.</summary>
public static class ChatMonitoringVectorKeys
{
    public static string LineagePositionId(NeuralModelKindChatMonitoring kind) =>
        $"chat-monitoring-{kind.ToString().ToLowerInvariant()}";
}
