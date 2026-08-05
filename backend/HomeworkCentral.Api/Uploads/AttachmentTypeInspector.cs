using MimeDetective;
using MimeDetective.Engine;
using MimeDetective.Storage;

namespace HomeworkCentral.Api.Uploads;

public sealed class AttachmentTypeInspector(
    IContentInspector contentInspector,
    HazardDefinitionRegistry hazardRegistry) : IAttachmentTypeInspector
{
    public AttachmentTypeInspectionResult Inspect(Stream stream, string? browserContentType)
    {
        byte[] head = ReadHead(stream, maxBytes: 8192);
        IEnumerable<DefinitionMatch> matches = contentInspector.Inspect(head);
        Definition? topMatch = matches
            .OrderByDescending(match => match.Points)
            .Select(match => match.Definition)
            .FirstOrDefault();

        string contentType = ResolveContentType(topMatch, browserContentType);
        bool isHazard = topMatch switch
        {
            { File.MimeType: string mime } => hazardRegistry.IsHazardMime(mime),
            _ => ShebangClassifier.IsScriptContent(head)
                || hazardRegistry.IsHazardMime(contentType),
        };

        string? inlineKind = ResolveInlineKind(contentType, isHazard);
        bool supportsInline = inlineKind is not null;

        return new AttachmentTypeInspectionResult(
            contentType,
            isHazard,
            supportsInline,
            inlineKind);
    }

    private static string? ResolveInlineKind(string contentType, bool isHazard)
    {
        if (isHazard)
            return null;

        ReadOnlySpan<char> mime = contentType.AsSpan();
        return mime switch
        {
            _ when mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => "image",
            _ when mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase) => "video",
            _ when mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) => "audio",
            _ when mime.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) => "pdf",
            _ when mime.StartsWith("text/", StringComparison.OrdinalIgnoreCase) => "text",
            _ => null,
        };
    }

    private static string ResolveContentType(Definition? match, string? browserContentType)
    {
        return match switch
        {
            { File.MimeType: string detectedMime } when !string.IsNullOrWhiteSpace(detectedMime) => detectedMime,
            _ => browserContentType switch
            {
                { Length: > 0 } mime when !mime.Equals(
                    "application/octet-stream",
                    StringComparison.OrdinalIgnoreCase) => mime,
                _ => "application/octet-stream",
            },
        };
    }

    private static byte[] ReadHead(Stream stream, int maxBytes)
    {
        if (!stream.CanSeek)
            throw new InvalidOperationException("Stream must be seekable for inspection.");

        long original = stream.Position;
        stream.Position = 0;
        byte[] buffer = new byte[Math.Min(maxBytes, stream.Length > 0 ? stream.Length : maxBytes)];
        int read = stream.Read(buffer, 0, buffer.Length);
        stream.Position = original;
        return buffer.AsSpan(0, read).ToArray();
    }
}
