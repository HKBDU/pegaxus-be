using System.Globalization;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Common;

public static class Vnd
{
    public const string Currency = "VND";

    public static bool IsWhole(decimal value) => ((decimal.GetBits(value)[3] >> 16) & 0xFF) == 0;

    public static decimal Tax(decimal subtotal) => decimal.Round(subtotal * 0.20m, 0, MidpointRounding.AwayFromZero);

    public static long ToLong(decimal value) => value == decimal.Truncate(value)
        ? (long)value
        : throw new UnprocessableEntityException("A stored amount is not a whole number of VND", "AMOUNT_NOT_WHOLE_VND");

    public static int ToInt(decimal value) => value == decimal.Truncate(value) && value is >= int.MinValue and <= int.MaxValue
        ? (int)value
        : throw new UnprocessableEntityException("A stored quantity is not a whole number", "AMOUNT_NOT_WHOLE_VND");

    public static string Format(decimal value, string currency = Currency) =>
        value.ToString("N0", CultureInfo.InvariantCulture) + " " + currency;
}
