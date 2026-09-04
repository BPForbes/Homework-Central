namespace HomeworkCentral.Api.Uploads;

/// <summary>
/// Validates attachment object keys so local-disk and S3 backends reject rooted paths
/// and parent-segment traversal before any I/O.
/// </summary>
public static class AttachmentStorageKeys
{
    public static bool IsValidObjectKey(string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || Path.IsPathRooted(storageKey))
            return false;

        string normalized = storageKey
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        return !normalized.Contains(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !normalized.EndsWith("..", StringComparison.Ordinal);
    }

    public static string NormalizeObjectKey(string storageKey)
    {
        if (!IsValidObjectKey(storageKey))
            throw new InvalidOperationException("Attachment storage path is invalid.");

        return storageKey
            .Replace(Path.AltDirectorySeparatorChar, '/')
            .TrimStart('/');
    }

    public static bool TryResolveLocalPath(string rootPath, string relativeStoragePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (!IsValidObjectKey(relativeStoragePath))
            return false;

        string normalizedRelative = relativeStoragePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        string rootFull = Path.GetFullPath(rootPath);
        string candidateFull = Path.GetFullPath(
            rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar
            + normalizedRelative);
        string rootPrefix = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!candidateFull.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(candidateFull, rootFull, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        fullPath = candidateFull;
        return true;
    }
}
