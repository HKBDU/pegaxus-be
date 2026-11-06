namespace Pegaxus.Service.Common;

public static class Decimals
{
    /// <summary>Number of decimal places, ignoring trailing zeros (1.50 has scale 1).</summary>
    public static int ScaleOf(decimal value) =>
        (decimal.GetBits(value / 1.0000000000000000000000000000m)[3] >> 16) & 0xFF;
}
