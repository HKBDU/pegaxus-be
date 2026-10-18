using FluentValidation;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.VehicleService.Validator;

public sealed class ListRequestValidator : AbstractValidator<Request.ListRequest>
{
    public ListRequestValidator()
    {
        RuleFor(x => x.PageIndex).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x).Must(x => ((long)x.PageIndex - 1) * x.PageSize <= int.MaxValue)
            .WithMessage("The requested page is too large.").OverridePropertyName("pageIndex");
        RuleFor(x => x.TransportMode).Must(x => x is null || Enum.GetNames<TransportMode>().Contains(x.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Transport mode must be ROAD, SEA or AIR.");
        RuleFor(x => x.Status).Must(x => x is null || Enum.GetNames<VehicleStatus>().Contains(x.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Invalid vehicle status.");
        RuleFor(x => x.MinCapacity).InclusiveBetween(2, 9).When(x => x.MinCapacity.HasValue);
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.VehicleType).MaximumLength(100);
        RuleFor(x => x.Country).Must(x => string.IsNullOrWhiteSpace(x) || CountryCodes.IsValid(x))
            .WithMessage("Country must be a 2-letter ISO code.");
        RuleFor(x => x).Must(x => x.AvailableFrom.HasValue == x.AvailableTo.HasValue &&
            (!x.AvailableFrom.HasValue || x.AvailableFrom <= x.AvailableTo && x.AvailableTo < DateOnly.MaxValue))
            .WithMessage("availableTo must not be earlier than availableFrom, and both are required together.")
            .WithErrorCode("INVALID_DATE_RANGE").OverridePropertyName("availableFrom");
        RuleFor(x => x.SortBy).Must(x => new[] { "vehiclecode:asc", "vehiclecode:desc", "capacityhorses:asc",
            "capacityhorses:desc", "updatedat:asc", "updatedat:desc" }.Contains(x?.Trim().ToLowerInvariant()))
            .WithMessage("The sort parameter is invalid.").WithErrorCode("INVALID_SORT_PARAMETER");
    }
}

public sealed class VehicleFieldsValidator : AbstractValidator<Request.VehicleFields>
{
    public VehicleFieldsValidator(TimeProvider clock)
    {
        RuleFor(x => x.TransportMode).Must(x => Enum.GetNames<TransportMode>().Contains(x?.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Transport mode must be ROAD, SEA or AIR.");
        RuleFor(x => x.LicensePlate).NotEmpty()
            .When(x => string.Equals(x.TransportMode?.Trim(), "ROAD", StringComparison.OrdinalIgnoreCase));
        RuleFor(x => x.LicensePlate).MaximumLength(50)
            .Matches(@"^[\p{L}\p{N}][\p{L}\p{N} .-]*$").WithMessage("Invalid license plate format.")
            .When(x => !string.IsNullOrWhiteSpace(x.LicensePlate));
        RuleFor(x => x.VehicleName).MaximumLength(200);
        RuleFor(x => x.VehicleType).MaximumLength(100);
        RuleFor(x => x.Manufacturer).MaximumLength(100);
        RuleFor(x => x.Model).MaximumLength(100);
        RuleFor(x => x.Country).Must(x => string.IsNullOrWhiteSpace(x) || CountryCodes.IsValid(x))
            .WithMessage("Country must be a 2-letter ISO code.");
        RuleFor(x => x.RegistrationNumber).MaximumLength(100);
        RuleFor(x => x.InsuranceNumber).MaximumLength(100);
        RuleFor(x => x.CapacityHorses).NotNull().InclusiveBetween(2, 9)
            .WithMessage("Vehicle capacity must be between 2 and 9 racehorses (BR-BOOKING-02).");
        RuleFor(x => x.RegistrationExpiryDate).Must(x => !x.HasValue || x.Value > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
            .WithMessage("Expiry date must be in the future.");
        RuleFor(x => x.InsuranceExpiryDate).Must(x => !x.HasValue || x.Value > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
            .WithMessage("Expiry date must be in the future.");
    }
}

public sealed class CreateRequestValidator : AbstractValidator<Request.CreateRequest>
{
    public CreateRequestValidator(TimeProvider clock)
    {
        Include(new VehicleFieldsValidator(clock));
        RuleFor(x => x.VehicleCode).NotEmpty().MaximumLength(50).Matches(@"^[A-Za-z0-9][A-Za-z0-9_-]*$")
            .WithMessage("Invalid vehicle code format.");
    }
}

public sealed class UpdateRequestValidator : AbstractValidator<Request.UpdateRequest>
{
    public UpdateRequestValidator(TimeProvider clock)
    {
        Include(new VehicleFieldsValidator(clock));
        RuleFor(x => x.LastUpdatedAt).NotEmpty();
        RuleFor(x => x.VehicleCode).MaximumLength(50);
        RuleFor(x => x.Status).MaximumLength(50);
    }
}

public sealed class StatusRequestValidator : AbstractValidator<Request.StatusRequest>
{
    public StatusRequestValidator()
    {
        RuleFor(x => x.LastUpdatedAt).NotEmpty();
        RuleFor(x => x.Status).Must(x => new[] { "AVAILABLE", "MAINTENANCE", "INACTIVE" }
            .Contains(x?.Trim(), StringComparer.OrdinalIgnoreCase)).WithMessage("Only manual vehicle statuses are allowed.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
    }
}
