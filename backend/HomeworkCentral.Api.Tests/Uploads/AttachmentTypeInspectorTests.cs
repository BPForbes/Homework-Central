using HomeworkCentral.Api.Uploads;
using MimeDetective;

namespace HomeworkCentral.Api.Tests.Uploads;

public class AttachmentTypeInspectorTests
{
    private static AttachmentTypeInspector CreateInspector()
    {
        HazardDefinitionRegistry hazardRegistry = new();
        IContentInspector contentInspector = new ContentInspectorBuilder
        {
            Definitions = MimeDetective.Definitions.DefaultDefinitions.All(),
        }.Build();
        return new AttachmentTypeInspector(contentInspector, hazardRegistry);
    }

    [Fact]
    public void Inspect_png_is_image_and_not_hazard()
    {
        byte[] png =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        ];
        AttachmentTypeInspector inspector = CreateInspector();

        using MemoryStream stream = new(png);
        AttachmentTypeInspectionResult result = inspector.Inspect(stream, "application/octet-stream");

        Assert.StartsWith("image/", result.ContentType, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.IsHazard);
        Assert.True(result.SupportsInlinePreview);
        Assert.Equal("image", result.InlinePreviewKind);
    }

    [Fact]
    public void Inspect_pdf_is_inline_and_not_hazard()
    {
        byte[] pdf = "%PDF-1.4\n"u8.ToArray();
        AttachmentTypeInspector inspector = CreateInspector();

        using MemoryStream stream = new(pdf);
        AttachmentTypeInspectionResult result = inspector.Inspect(stream, "application/octet-stream");

        Assert.Equal("application/pdf", result.ContentType, StringComparer.OrdinalIgnoreCase);
        Assert.False(result.IsHazard);
        Assert.Equal("pdf", result.InlinePreviewKind);
    }

    [Fact]
    public void Inspect_zip_is_hazard()
    {
        byte[] zip = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00];
        AttachmentTypeInspector inspector = CreateInspector();

        using MemoryStream stream = new(zip);
        AttachmentTypeInspectionResult result = inspector.Inspect(stream, "application/octet-stream");

        Assert.True(result.IsHazard);
        Assert.Null(result.InlinePreviewKind);
        Assert.False(result.SupportsInlinePreview);
    }

    [Fact]
    public void Inspect_pe_executable_is_hazard()
    {
        byte[] pe = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00];
        AttachmentTypeInspector inspector = CreateInspector();

        using MemoryStream stream = new(pe);
        AttachmentTypeInspectionResult result = inspector.Inspect(stream, "application/octet-stream");

        Assert.True(result.IsHazard);
        Assert.Null(result.InlinePreviewKind);
    }

    [Fact]
    public void Inspect_shebang_script_is_hazard_without_magic()
    {
        byte[] script = "#!/usr/bin/env python3\nprint('hi')\n"u8.ToArray();
        AttachmentTypeInspector inspector = CreateInspector();

        using MemoryStream stream = new(script);
        AttachmentTypeInspectionResult result = inspector.Inspect(stream, "text/plain");

        Assert.True(result.IsHazard);
    }
}
