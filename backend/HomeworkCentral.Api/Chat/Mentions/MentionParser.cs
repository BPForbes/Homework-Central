using System.Text;
using System.Text.RegularExpressions;

namespace HomeworkCentral.Api.Chat.Mentions;

/// <summary>
/// Parses @-mentions in chat messages. Mentions wrapped in double backticks (``@token``) are
/// rendered as plain text and do not ping. Content inside any other Markdown backtick code
/// delimiter (`inline code`, ```fences```) is kept verbatim — the frontend Markdown renderer
/// owns its presentation — and never pings. Unauthorized @everyone/@here tokens are replaced
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

        List<EscapedRange> escapedRanges = FindEscapedRanges(rawContent);
        StringBuilder display = new(rawContent.Length);
        List<ParsedMention> activeMentions = [];
        bool containsAnyMentionToken = false;
        int cursor = 0;

        foreach ((int start, int end, bool stripDelimiters) in escapedRanges)
        {
            ProcessPlainSegment(rawContent, cursor, start, canUseBroadcastMentions, display, activeMentions, ref containsAnyMentionToken);
            display.Append(stripDelimiters ? rawContent[(start + 2)..(end - 2)] : rawContent[start..end]);
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

    private readonly record struct EscapedRange(int Start, int End, bool StripDelimiters);

    /// <summary>
    /// Finds backtick-delimited spans, pairing each opening backtick run with the next run of
    /// exactly the same length (Markdown code-span semantics — so a ```fence``` is one span and
    /// a `` opener can never latch onto the first two backticks of a fence, which used to
    /// destroy fenced code blocks). Double-backtick spans are mention escapes whose delimiters
    /// get stripped; every other matched span is Markdown code and is kept verbatim.
    /// </summary>
    private static List<EscapedRange> FindEscapedRanges(string content)
    {
        List<EscapedRange> ranges = [];
        int index = 0;
        while (index < content.Length)
        {
            if (content[index] != '`')
            {
                index++;
                continue;
            }

            int runStart = index;
            while (index < content.Length && content[index] == '`')
                index++;
            int runLength = index - runStart;

            int close = FindClosingRun(content, index, runLength);
            if (close < 0)
                continue;

            ranges.Add(new EscapedRange(runStart, close + runLength, StripDelimiters: runLength == 2));
            index = close + runLength;
        }

        return ranges;
    }

    private static int FindClosingRun(string content, int searchFrom, int runLength)
    {
        string delimiter = new('`', runLength);
        int search = searchFrom;
        while (search < content.Length)
        {
            int close = content.IndexOf(delimiter, search, StringComparison.Ordinal);
            if (close < 0)
                return -1;

            int runEnd = close;
            while (runEnd < content.Length && content[runEnd] == '`')
                runEnd++;

            // Only a run of exactly the opening length closes the span.
            if (runEnd - close == runLength && close > searchFrom)
                return close;

            search = runEnd;
        }

        return -1;
    }
}
