using FluentValidation;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.TransportProviderService.Validator;

public sealed class ListRequestValidator : AbstractValidator<Request.ListRequest>
{
    public ListRequestValidator()
    {
        RuleFor(request => request.PageIndex).GreaterThan(0);
        RuleFor(request => request.PageSize).InclusiveBetween(1, 100);
        RuleFor(request => request).Must(request => ((long)request.PageIndex - 1) * request.PageSize <= int.MaxValue)
            .WithMessage("Pagination exceeds the supported range.");
        RuleFor(request => request.Status).Must(value => string.IsNullOrWhiteSpace(value) || StatusRequestValidator.IsStatus(value));
        RuleFor(request => request.SupportedMode).Must(value => string.IsNullOrWhiteSpace(value) || SaveRequestValidator.IsMode(value));
        RuleFor(request => request.Country).Must(value => string.IsNullOrWhiteSpace(value) || CountryCodes.IsValid(value))
            .WithMessage("Country must be a 2-letter ISO code.");
        RuleFor(request => request.Search).MaximumLength(200);
        RuleFor(request => request.SortBy).Must(value => value?.Trim().ToLowerInvariant() is
            "providername:asc" or "providername:desc" or "providercode:asc" or "providercode:desc" or "createdat:asc" or "createdat:desc");
    }
}

public sealed class SaveRequestValidator : AbstractValidator<Request.SaveRequest>
{
    public SaveRequestValidator()
    {
        RuleFor(request => request.ProviderName).Must(value => !string.IsNullOrWhiteSpace(value)).MaximumLength(200);
        RuleFor(request => request.TaxCode).Must(value => !string.IsNullOrWhiteSpace(value)).MaximumLength(100);
        RuleFor(request => request.SupportedModes).NotEmpty();
        RuleForEach(request => request.SupportedModes).Must(IsMode).WithMessage("Supported modes must be ROAD, SEA or AIR.");
        RuleFor(request => request.ContactName).MaximumLength(150);
        RuleFor(request => request.Phone).MaximumLength(30).Must(Phones.IsValid)
            .When(request => !string.IsNullOrWhiteSpace(request.Phone)).WithMessage(Phones.Message);
        RuleFor(request => request.Email).MaximumLength(255).EmailAddress().When(request => !string.IsNullOrWhiteSpace(request.Email));
        RuleFor(request => request.Address).MaximumLength(500);
        RuleFor(request => request.Country).Must(value => string.IsNullOrWhiteSpace(value) || CountryCodes.IsValid(value))
            .WithMessage("Country must be a 2-letter ISO code.");
        RuleFor(request => request.Note).MaximumLength(2000);
    }

    internal static bool IsMode(string? value) => value?.Trim().ToUpperInvariant() is "ROAD" or "SEA" or "AIR";
}

public sealed class CreateRequestValidator : AbstractValidator<Request.CreateRequest>
{
    public CreateRequestValidator()
    {
        Include(new SaveRequestValidator());
    }
}

public sealed class UpdateRequestValidator : AbstractValidator<Request.UpdateRequest>
{
    public UpdateRequestValidator()
    {
        Include(new SaveRequestValidator());
        RuleFor(request => request.LastUpdatedAt).NotEmpty();
    }
}

public sealed class StatusRequestValidator : AbstractValidator<Request.StatusRequest>
{
    public StatusRequestValidator()
    {
        RuleFor(request => request.Status).Must(IsStatus).WithMessage("Status must be ACTIVE, INACTIVE or SUSPENDED.");
        RuleFor(request => request.LastUpdatedAt).NotEqual(default(DateTimeOffset)).When(request => request.LastUpdatedAt.HasValue);
        RuleFor(request => request.Reason).MaximumLength(2000);
        RuleFor(request => request.Reason).Must(value => !string.IsNullOrWhiteSpace(value))
            .When(request => request.Status?.Trim().ToUpperInvariant() is "INACTIVE" or "SUSPENDED")
            .WithMessage("A reason is required when making a provider INACTIVE or SUSPENDED.");
    }

    internal static bool IsStatus(string? value) => value?.Trim().ToUpperInvariant() is "ACTIVE" or "INACTIVE" or "SUSPENDED";
}
