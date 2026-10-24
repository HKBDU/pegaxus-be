using System.Globalization;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Common;

public static class Vnd
{
    public const string Currency = "VND";

    public static bool IsWhole(decimal value) => ((decimal.GetBits(value)[3] >> 16) & 0xFF) == 0;

    public static decimal Tax(decimal subtotal) => decimal.Round(subtotal * 0.20m, 0, MidpointRounding.AwayFromZero);

    /// <summary>A credit note is compensation paid to the customer, so it carries no VAT (flow6 R8, O-F6-10); every other type is taxed at 20%.</summary>
    public static int TaxRatePercent(InvoiceType type) => type == InvoiceType.CREDIT_NOTE ? 0 : 20;

    public static decimal Tax(decimal subtotal, InvoiceType type) => type == InvoiceType.CREDIT_NOTE ? 0 : Tax(subtotal);

    public static long ToLong(decimal value) => value == decimal.Truncate(value)
        ? (long)value
        : throw new UnprocessableEntityException("A stored amount is not a whole number of VND", "AMOUNT_NOT_WHOLE_VND");

    public static int ToInt(decimal value) => value == decimal.Truncate(value) && value is >= int.MinValue and <= int.MaxValue
        ? (int)value
        : throw new UnprocessableEntityException("A stored quantity is not a whole number", "AMOUNT_NOT_WHOLE_VND");

    public static string Format(decimal value, string currency = Currency) =>
        value.ToString("N0", CultureInfo.InvariantCulture) + " " + currency;
}
