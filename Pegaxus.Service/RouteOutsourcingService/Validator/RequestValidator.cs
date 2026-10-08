using System.Globalization;
using FluentValidation;

namespace Pegaxus.Service.RouteOutsourcingService.Validator;

public sealed class RequestValidator : AbstractValidator<Request>
{
    private static readonly HashSet<string> CurrencyCodes = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(culture => new RegionInfo(culture.Name).ISOCurrencySymbol).Where(code => code != "XXX").ToHashSet(StringComparer.OrdinalIgnoreCase);

    public RequestValidator()
    {
        RuleFor(request => request.Outsourced).NotNull();
        RuleFor(request => request.LastUpdatedAt).NotEmpty();
        RuleFor(request => request.TransportProviderId).Must(id => id.HasValue && id != Guid.Empty)
            .When(request => request.Outsourced == true).WithErrorCode("PROVIDER_REQUIRED")
            .WithMessage("Please select a valid Transport Provider for this outsourced segment.");
        RuleFor(request => request.ServiceType).Must(value => !string.IsNullOrWhiteSpace(value))
            .When(request => request.Outsourced == true);
        RuleFor(request => request.ServiceType).MaximumLength(100);
        RuleFor(request => request.ProviderReference).MaximumLength(255);
        RuleFor(request => request.CostAmount).NotNull().InclusiveBetween(0m, 9999999999999999.99m)
            .Must(amount => amount is null || decimal.Round(amount.Value, 2) == amount.Value)
            .When(request => request.Outsourced == true).WithMessage("Cost amount is required, non-negative and limited to 2 decimal places.");
        RuleFor(request => request.Currency).Must(currency => currency is not null && CurrencyCodes.Contains(currency.Trim()))
            .When(request => request.Outsourced == true).WithMessage("A recognized ISO 4217 currency code is required.");
        RuleFor(request => request).Must(request => request.Outsourced != false ||
            (request.TransportProviderId is null && request.ServiceType is null && request.ProviderReference is null && request.CostAmount is null && request.Currency is null))
            .WithMessage("Internal segments cannot include provider, service, reference or cost fields.");
    }
}
