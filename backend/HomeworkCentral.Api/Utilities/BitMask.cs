using System.Collections;

namespace HomeworkCentral.Api.Utilities;

public static class BitMask
{
    public static BitArray Create(int length) => new(length);

    public static BitArray Or(BitArray left, BitArray right)
    {
        var length = Math.Max(left.Length, right.Length);
        var result = new BitArray(length);
        for (var i = 0; i < length; i++)
            result[i] = GetBit(left, i) || GetBit(right, i);
        return result;
    }

    public static BitArray Or(IEnumerable<BitArray> masks)
    {
        BitArray? combined = null;
        foreach (var mask in masks)
        {
            if (combined is null)
                combined = (BitArray)mask.Clone();
            else
                combined = Or(combined, mask);
        }

        return combined ?? new BitArray(0);
    }

    public static bool HasBit(BitArray mask, int bit) =>
        bit >= 0 && bit < mask.Length && mask[bit];

    public static void SetBit(BitArray mask, int bit, bool value = true)
    {
        if (bit < 0 || bit >= mask.Length)
            throw new ArgumentOutOfRangeException(nameof(bit));
        mask[bit] = value;
    }

    public static string ToBase64(BitArray mask)
    {
        var byteCount = (mask.Length + 7) / 8;
        var bytes = new byte[byteCount];
        mask.CopyTo(bytes, 0);
        return Convert.ToBase64String(bytes);
    }

    public static BitArray FromBase64(string base64, int length)
    {
        var bytes = Convert.FromBase64String(base64);
        var mask = new BitArray(length);
        for (var i = 0; i < length; i++)
        {
            var byteIndex = i / 8;
            var bitIndex = i % 8;
            mask[i] = byteIndex < bytes.Length && (bytes[byteIndex] & (1 << bitIndex)) != 0;
        }

        return mask;
    }

    private static bool GetBit(BitArray mask, int index) =>
        index >= 0 && index < mask.Length && mask[index];
}
