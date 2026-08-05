using HomeworkCentral.Api.Uploads;

namespace HomeworkCentral.Api.Tests.Uploads;

public sealed class AttachmentStorageKeysTests
{
    [Theory]
    [InlineData("abc123_report.pdf")]
    [InlineData("folder/file.bin")]
    public void IsValidObjectKey_AcceptsRelativeKeys(string key)
    {
        Assert.True(AttachmentStorageKeys.IsValidObjectKey(key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/rooted")]
    [InlineData("../escape")]
    [InlineData("a/../../b")]
    [InlineData("trailing-parent..")]
    public void IsValidObjectKey_RejectsUnsafeKeys(string key)
    {
        Assert.False(AttachmentStorageKeys.IsValidObjectKey(key));
    }

    [Fact]
    public void NormalizeObjectKey_TrimsLeadingSeparators()
    {
        string normalized = AttachmentStorageKeys.NormalizeObjectKey("folder/file.bin");
        Assert.Equal("folder/file.bin", normalized);
    }
}
