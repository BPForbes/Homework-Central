using System.Collections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HomeworkCentral.Api.Data;

internal static class BitArrayConfiguration
{
    public static PropertyBuilder<BitArray> HasBitColumn(
        this PropertyBuilder<BitArray> property,
        string columnType,
        int defaultLength)
    {
        return property
            .HasColumnType(columnType)
            .IsRequired()
            .HasConversion(
                v => v,
                v => v ?? new BitArray(defaultLength));
    }
}
