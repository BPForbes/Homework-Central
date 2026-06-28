using System.Collections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HomeworkCentral.Api.Data;

internal static class BitArrayConfiguration
{
    private static readonly ValueComparer<BitArray> BitArrayComparer = new(
        (BitArray? left, BitArray? right) => CompareBitArrays(left, right),
        mask => GetBitArrayHash(mask),
        mask => (BitArray)mask.Clone());

    public static PropertyBuilder<BitArray> HasBitColumn(
        this PropertyBuilder<BitArray> property,
        string columnType,
        int defaultLength)
    {
        PropertyBuilder<BitArray> builder = property
            .HasColumnType(columnType)
            .IsRequired()
            .HasConversion(
                v => v,
                v => v ?? new BitArray(defaultLength));

        builder.Metadata.SetValueComparer(BitArrayComparer);
        return builder;
    }

    private static bool CompareBitArrays(BitArray? left, BitArray? right)
    {
        if (left is null || right is null)
            return left == right;

        if (left.Length != right.Length)
            return false;

        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
                return false;
        }

        return true;
    }

    private static int GetBitArrayHash(BitArray mask)
    {
        HashCode hash = new();
        for (int i = 0; i < mask.Length; i++)
            hash.Add(mask[i]);
        return hash.ToHashCode();
    }
}
