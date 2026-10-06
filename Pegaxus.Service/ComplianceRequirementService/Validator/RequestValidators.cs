using FluentValidation;

namespace Pegaxus.Service.ComplianceRequirementService.Validator;

public sealed class ListRequestValidator : AbstractValidator<Request.ListRequest>
{
    public ListRequestValidator()
    {
        RuleFor(request => request.PageIndex).GreaterThan(0);
        RuleFor(request => request.PageSize).InclusiveBetween(1, 100);
        RuleFor(request => request.Search).MaximumLength(200);
        RuleFor(request => request.BcpCode).MaximumLength(50);
        RuleFor(request => request.Country).Must(SaveRequestValidator.IsValidCountry)
            .WithMessage("Country must be an EU member ISO code, GB or EU");
        RuleFor(request => request.Category).Must(SaveRequestValidator.IsValidCategory)
            .When(request => !string.IsNullOrWhiteSpace(request.Category))
            .WithMessage("Category must be QUARANTINE, CUSTOMS, TRANSPORT, HORSE_HEALTH, HORSE_IDENTITY or OTHER");
        RuleFor(request => request.Status).Must(StatusRequestValidator.IsValidStatus)
            .When(request => !string.IsNullOrWhiteSpace(request.Status))
            .WithMessage("Status must be ACTIVE or INACTIVE");
        RuleFor(request => request.SortBy).Must(value => value?.Trim().ToLowerInvariant() is
            "updatedat:asc" or "updatedat:desc" or "requirementname:asc" or "requirementname:desc" or
            "requirementcode:asc" or "requirementcode:desc")
            .WithMessage("SortBy must be updatedAt, requirementName or requirementCode followed by :asc or :desc");
    }
}

public sealed class SaveRequestValidator : AbstractValidator<Request.SaveRequest>
{
    private static readonly HashSet<string> Countries = ["EU", "GB", "AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR", "DE", "GR", "HU", "IE", "IT", "LV", "LT", "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE"];

    public SaveRequestValidator()
    {
        RuleFor(request => request.RequirementCode).NotEmpty().MaximumLength(50);
        RuleFor(request => request.RequirementName).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Country).Must(IsValidCountry)
            .WithMessage("Country must be an EU member ISO code, GB or EU");
        RuleFor(request => request.Category).Must(IsValidCategory)
            .WithMessage("Category must be QUARANTINE, CUSTOMS, TRANSPORT, HORSE_HEALTH, HORSE_IDENTITY or OTHER");
        RuleFor(request => request.Status).Must(StatusRequestValidator.IsValidStatus)
            .WithMessage("Status must be ACTIVE or INACTIVE");
        RuleFor(request => request.BcpCode).MaximumLength(50);
        RuleFor(request => request.IssuingAuthority).MaximumLength(200);
        RuleFor(request => request.ValidityRequired).NotNull();
        RuleFor(request => request.ValidityPeriodDays).GreaterThan(0);
        RuleFor(request => request.ValidityPeriodDays).NotNull().When(request => request.ValidityRequired == true)
            .WithMessage("ValidityPeriodDays is required when ValidityRequired is true");
    }

    internal static bool IsValidCountry(string? value) => string.IsNullOrWhiteSpace(value) || Countries.Contains(value.Trim().ToUpperInvariant());
    internal static bool IsValidCategory(string? value) => value?.Trim().ToUpperInvariant() is
        "QUARANTINE" or "CUSTOMS" or "TRANSPORT" or "HORSE_HEALTH" or "HORSE_IDENTITY" or "OTHER";
}

public sealed class StatusRequestValidator : AbstractValidator<Request.StatusRequest>
{
    public StatusRequestValidator()
    {
        RuleFor(request => request.Status).Must(IsValidStatus).WithMessage("Status must be ACTIVE or INACTIVE");
    }

    internal static bool IsValidStatus(string? value) => value?.Trim().ToUpperInvariant() is "ACTIVE" or "INACTIVE";
}
