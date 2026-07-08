using System.Text;
using System.Text.RegularExpressions;

namespace HomeworkCentral.Api.Chat.Mentions;

/// <summary>
/// Parses @-mentions in chat messages. Mentions wrapped in double backticks (``@token``) are
/// rendered as plain text and do not ping. Unauthorized @everyone/@here tokens are replaced
/// with @null (plain text, no ping). User vs role disambiguation for tokens that match both a
/// username and a mentionable role happens at recipient resolution time (user match first).
/// </summary>
public static partial class MentionParser
{
    public const string NullToken = "null";
    public const string EveryoneToken = "everyone";
    public const string HereToken = "here";

    [GeneratedRegex(@"@([\p{L}\p{N}_][\p{L}\p{N}_.-]{0,63})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex MentionTokenRegex();

    public static MentionParseResult Parse(string rawContent, bool canUseBroadcastMentions)
    {
        if (string.IsNullOrEmpty(rawContent))
            return new MentionParseResult(rawContent, [], false);

        List<(int Start, int End)> escapedRanges = FindEscapedRanges(rawContent);
        StringBuilder display = new(rawContent.Length);
        List<ParsedMention> activeMentions = [];
        bool containsAnyMentionToken = false;
        int cursor = 0;

        foreach ((int start, int end) in escapedRanges)
        {
            ProcessPlainSegment(rawContent, cursor, start, canUseBroadcastMentions, display, activeMentions, ref containsAnyMentionToken);
            display.Append(rawContent[(start + 2)..(end - 2)]);
            cursor = end;
        }

        ProcessPlainSegment(rawContent, cursor, rawContent.Length, canUseBroadcastMentions, display, activeMentions, ref containsAnyMentionToken);

        return new MentionParseResult(display.ToString(), activeMentions, containsAnyMentionToken);
    }

    private static void ProcessPlainSegment(
        string rawContent,
        int segmentStart,
        int segmentEnd,
        bool canUseBroadcastMentions,
        StringBuilder display,
        List<ParsedMention> activeMentions,
        ref bool containsAnyMentionToken)
    {
        if (segmentStart >= segmentEnd)
            return;

        string segment = rawContent[segmentStart..segmentEnd];
        int lastIndex = 0;

        foreach (Match match in MentionTokenRegex().Matches(segment))
        {
            containsAnyMentionToken = true;
            display.Append(segment[lastIndex..match.Index]);

            string token = match.Groups[1].Value;
            MentionKind? kind = ClassifyToken(token);
            int outputStart = display.Length;

            if (kind is null)
            {
                display.Append(segment[match.Index..(match.Index + match.Length)]);
            }
            else if (kind is MentionKind.Everyone or MentionKind.Here && !canUseBroadcastMentions)
            {
                string replacement = $"@{NullToken}";
                display.Append(replacement);
                activeMentions.Add(new ParsedMention(kind.Value, NullToken, outputStart, replacement.Length, IsActive: false));
            }
            else
            {
                display.Append(segment[match.Index..(match.Index + match.Length)]);
                activeMentions.Add(new ParsedMention(kind.Value, token, outputStart, match.Length, IsActive: true));
            }

            lastIndex = match.Index + match.Length;
        }

        display.Append(segment[lastIndex..]);
    }

    private static MentionKind? ClassifyToken(string token)
    {
        if (string.Equals(token, EveryoneToken, StringComparison.OrdinalIgnoreCase))
            return MentionKind.Everyone;

        if (string.Equals(token, HereToken, StringComparison.OrdinalIgnoreCase))
            return MentionKind.Here;

        if (string.Equals(token, NullToken, StringComparison.OrdinalIgnoreCase))
            return null;

        return MentionKind.User;
    }

    private static List<(int Start, int End)> FindEscapedRanges(string content)
    {
        List<(int Start, int End)> ranges = [];
        int index = 0;
        while (index < content.Length - 3)
        {
            if (content[index] == '`' && content[index + 1] == '`')
            {
                int close = content.IndexOf("``", index + 2, StringComparison.Ordinal);
                if (close > index + 2)
                {
                    ranges.Add((index, close + 2));
                    index = close + 2;
                    continue;
                }
            }

            index++;
        }

        return ranges;
    }
}
