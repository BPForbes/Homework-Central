using MimeDetective.Definitions;
using MimeDetective.Storage;

namespace HomeworkCentral.Api.Uploads;

public sealed class HazardDefinitionRegistry
{
    private readonly HashSet<string> _hazardMimeTypes;

    public HazardDefinitionRegistry()
    {
        IEnumerable<Definition> hazardDefinitions =
            DefaultDefinitions.FileTypes.Executables.All()
            .Concat(DefaultDefinitions.FileTypes.Archives.All());

        _hazardMimeTypes = hazardDefinitions
            .Select(definition => definition.File.MimeType)
            .Where(mime => !string.IsNullOrWhiteSpace(mime))
            .Select(mime => mime!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool IsHazardMime(string mimeType) =>
        _hazardMimeTypes.Contains(mimeType);
}
