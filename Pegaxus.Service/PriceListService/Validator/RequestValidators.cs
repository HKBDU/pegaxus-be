using System.Text.RegularExpressions;
using FluentValidation;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.InvoiceService;

namespace Pegaxus.Service.PriceListService.Validator;

public sealed class ListRequestValidator : AbstractValidator<Request.ListRequest>
{
    public ListRequestValidator()
    {
        RuleFor(request => request.PageIndex).GreaterThan(0);
        RuleFor(request => request.PageSize).InclusiveBetween(1, 100);
        RuleFor(request => request.Search).MaximumLength(200);
        RuleFor(request => request.SortBy).Must(value => value?.Trim().ToLowerInvariant() is
            "createdat:asc" or "createdat:desc" or "updatedat:asc" or "updatedat:desc" or "name:asc" or "name:desc")
            .WithMessage("SortBy must be createdAt, updatedAt or name followed by :asc or :desc");
    }
}

internal static class PriceListRules
{
    public const long MaxUnitPrice = 10_000_000_000;

    public static void Header<T>(AbstractValidator<T> validator, Func<T, string?> name, Func<T, string?> currency,
        Func<T, decimal?> taxRate, Func<T, decimal?> depositRate)
    {
        validator.RuleFor(request => name(request)).NotEmpty().MaximumLength(200).OverridePropertyName("name");
        validator.RuleFor(request => currency(request)).Must(value => value is not null && Regex.IsMatch(value.Trim(), "^[A-Za-z]{3}$"))
            .WithMessage("Currency must be a 3-letter ISO 4217 code.").OverridePropertyName("currency");
        Rate(validator, taxRate, "taxRate", "Tax rate");
        Rate(validator, depositRate, "depositRate", "Deposit rate");
    }

    private static void Rate<T>(AbstractValidator<T> validator, Func<T, decimal?> rate, string field, string label) =>
        validator.RuleFor(request => rate(request)).Cascade(CascadeMode.Stop).NotNull()
            .Must(value => value is >= 0 and <= 100).WithMessage($"{label} must be between 0 and 100.")
            .Must(value => decimal.Round(value!.Value, 2) == value).WithMessage($"{label} can have at most 2 decimal places.")
            .OverridePropertyName(field);

    public static void Item<T>(AbstractValidator<T> validator, Func<T, string?> name, Func<T, string?> type, Func<T, string?> unit,
        Func<T, decimal?> price, Func<T, string?> tier, Func<T, int?> sortOrder)
    {
        validator.RuleFor(request => name(request)).NotEmpty().MaximumLength(200).OverridePropertyName("itemName");
        validator.RuleFor(request => type(request)).Must(value => ParseType(value) is not null)
            .WithMessage($"Item type must be one of: {string.Join(", ", Enum.GetNames<InvoiceItemType>())}.").OverridePropertyName("itemType");
        validator.RuleFor(request => unit(request)).Must(value => QuotationCatalog.Units.Contains(value?.Trim().ToUpperInvariant() ?? ""))
            .WithMessage($"Unit must be one of: {string.Join(", ", QuotationCatalog.Units)}.").OverridePropertyName("unit");
        validator.RuleFor(request => price(request)).Cascade(CascadeMode.Stop).NotNull()
            .Must(value => value is > 0 and <= MaxUnitPrice).WithMessage($"Unit price must be greater than 0 and at most {MaxUnitPrice}.")
            .Must(value => value == decimal.Truncate(value!.Value)).WithMessage("Unit price must be a whole number of VND.")
            .OverridePropertyName("unitPrice");
        validator.RuleFor(request => tier(request)).Must(value => string.IsNullOrWhiteSpace(value) || value.Trim().ToUpperInvariant() is "S" or "M" or "L")
            .WithMessage("Vehicle tier must be S, M or L.").OverridePropertyName("vehicleTier");
        validator.RuleFor(request => sortOrder(request)).Must(value => value is null or >= 0 and <= 1_000_000)
            .WithMessage("Sort order must be between 0 and 1000000.").OverridePropertyName("sortOrder");
    }

    public static InvoiceItemType? ParseType(string? value) =>
        Enum.TryParse<InvoiceItemType>(value?.Trim(), true, out var type) && Enum.IsDefined(type) && !int.TryParse(value?.Trim(), out _) ? type : null;
}

public sealed class CreateRequestValidator : AbstractValidator<Request.CreateRequest>
{
    public CreateRequestValidator() =>
        PriceListRules.Header(this, request => request.Name, request => request.Currency, request => request.TaxRate, request => request.DepositRate);
}

public sealed class UpdateRequestValidator : AbstractValidator<Request.UpdateRequest>
{
    public UpdateRequestValidator()
    {
        PriceListRules.Header(this, request => request.Name, request => request.Currency, request => request.TaxRate, request => request.DepositRate);
        RuleFor(request => request.LastUpdatedAt).NotNull().OverridePropertyName("lastUpdatedAt");
    }
}

public sealed class CreateItemRequestValidator : AbstractValidator<Request.CreateItemRequest>
{
    public CreateItemRequestValidator()
    {
        RuleFor(request => request.ItemCode).Must(value => value is not null && Regex.IsMatch(value, "^[A-Z0-9_]{1,50}$"))
            .WithMessage("Item code must be 1 to 50 characters of A-Z, 0-9 and underscore.").OverridePropertyName("itemCode");
        PriceListRules.Item(this, request => request.ItemName, request => request.ItemType, request => request.Unit,
            request => request.UnitPrice, request => request.VehicleTier, request => request.SortOrder);
    }
}

public sealed class UpdateItemRequestValidator : AbstractValidator<Request.UpdateItemRequest>
{
    public UpdateItemRequestValidator()
    {
        PriceListRules.Item(this, request => request.ItemName, request => request.ItemType, request => request.Unit,
            request => request.UnitPrice, request => request.VehicleTier, request => request.SortOrder);
        RuleFor(request => request.LastUpdatedAt).NotNull().OverridePropertyName("lastUpdatedAt");
    }
}
