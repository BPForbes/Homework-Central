using System.Collections;
using HomeworkCentral.Api.Utilities;

namespace HomeworkCentral.Api.Tests.Utilities;

public class BitMaskTests
{
    [Fact]
    public void Or_combines_set_bits_from_both_masks()
    {
        BitArray left = BitMask.Create(8);
        BitMask.SetBit(left, 1);
        BitArray right = BitMask.Create(8);
        BitMask.SetBit(right, 3);

        BitArray combined = BitMask.Or(left, right);

        Assert.True(BitMask.HasBit(combined, 1));
        Assert.True(BitMask.HasBit(combined, 3));
        Assert.False(BitMask.HasBit(combined, 0));
    }

    [Fact]
    public void ToBase64_round_trips_bit_values()
    {
        BitArray mask = BitMask.Create(16);
        BitMask.SetBit(mask, 0);
        BitMask.SetBit(mask, 7);
        BitMask.SetBit(mask, 15);

        string encoded = BitMask.ToBase64(mask);
        BitArray decoded = BitMask.FromBase64(encoded, 16);

        Assert.True(BitMask.HasBit(decoded, 0));
        Assert.True(BitMask.HasBit(decoded, 7));
        Assert.True(BitMask.HasBit(decoded, 15));
        Assert.False(BitMask.HasBit(decoded, 1));
    }
}
