using FluentValidation;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.IncidentService.Validator;

public sealed class VehicleFieldsValidator : AbstractValidator<Request.VehicleFields>
{
    public VehicleFieldsValidator()
    {
        RuleFor(request => request.VehicleCondition).MaximumLength(2000);
        RuleFor(request => request.DamageDescription).MaximumLength(2000);
        RuleFor(request => request.DriverSafetyStatus).MaximumLength(500);
        RuleFor(request => request.CargoConditionNote).MaximumLength(500);
        RuleFor(request => request.EstimatedRepairCost).Must(value => value is null || Vnd.IsWhole(value.Value))
            .WithMessage("Amount must be a whole number of VND");
        RuleFor(request => request.EstimatedRepairCost).InclusiveBetween(0, 9_999_999_999_999_999m)
            .WithMessage("Estimated repair cost must be non-negative and within numeric(18,2).");
        RuleFor(request => request.EstimatedRepairHours).Must(value => value is null || value is >= 0 and <= 999.9m && decimal.Round(value.Value, 1) == value)
            .WithMessage("Estimated repair hours must be between 0 and 999.9 with at most one decimal place.");
        RuleFor(request => request.PhotoFiles).Must(files => files is { Count: <= 5 })
            .WithMessage("At most 5 photos are accepted per request.");
        RuleFor(request => request.PhotoFiles).Must(files => files is null || files.All(file => file is not null &&
                !string.IsNullOrWhiteSpace(file.PublicId) && file.PublicId.Length <= 255) &&
                files.Select(file => file.PublicId).Distinct(StringComparer.Ordinal).Count() == files.Count)
            .WithMessage("Each photo must have a distinct, non-empty publicId of at most 255 characters.");
    }
}

public sealed class ReportVehicleRequestValidator : AbstractValidator<Request.ReportVehicleRequest>
{
    public ReportVehicleRequestValidator(TimeProvider clock, IValidator<Request.VehicleFields> vehicleValidator)
    {
        Include(vehicleValidator);
        RuleFor(request => request.TransportId).NotEmpty();
        RuleFor(request => request.VehicleId).NotEmpty();
        RuleFor(request => request.Category).Must(value => value is "VEHICLE" or "BOTH")
            .WithMessage("Category must be VEHICLE or BOTH.");
        RuleFor(request => request.IncidentType).Must(value => Enum.GetNames<VehicleIncidentType>().Contains(value))
            .WithMessage("A valid vehicle incident type is required.");
        RuleFor(request => request.Severity).Must(value => Enum.GetNames<IncidentSeverity>().Contains(value))
            .WithMessage("A valid incident severity is required.");
        RuleFor(request => request.VehicleOperable).NotNull();
        RuleFor(request => request.Title).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Description).Must(value => value?.Trim().Length >= 10).MaximumLength(2000)
            .WithMessage("Description must contain between 10 and 2000 characters after trimming.");
        RuleFor(request => request.ImmediateAction).MaximumLength(2000);
        RuleFor(request => request.OccurredAt).Must(value => value is null || value <= clock.GetUtcNow().AddMinutes(5))
            .WithMessage("Occurred at must not be more than 5 minutes in the future.");
        RuleFor(request => request.LocationId).NotEqual(Guid.Empty).When(request => request.LocationId.HasValue);
        RuleFor(request => request.LocationId).Must((request, id) => id.HasValue || request.Latitude.HasValue && request.Longitude.HasValue)
            .WithMessage("LocationId or both latitude and longitude are required.");
        RuleFor(request => request.Latitude).Must((request, latitude) => latitude is null && request.Longitude is null ||
                latitude.HasValue && request.Longitude.HasValue && Coordinates.IsValid(latitude.Value, request.Longitude.Value))
            .WithMessage("Coordinates must be a valid WGS84 pair with at most four decimal places and cannot be (0, 0).");
        RuleFor(request => request.LocationDescription).MaximumLength(500);
        RuleFor(request => request.Country).MaximumLength(100);
    }
}

public sealed class UpdateVehicleRequestValidator : AbstractValidator<Request.UpdateVehicleRequest>
{
    public UpdateVehicleRequestValidator(IValidator<Request.VehicleFields> vehicleValidator)
    {
        Include(vehicleValidator);
        RuleFor(request => request.LastUpdatedAt).NotNull();
        RuleFor(request => request.RepairNote).MaximumLength(2000);
        RuleFor(request => request).Must(request => request.RepairNote is not null || request.EstimatedRepairCost.HasValue ||
                request.VehicleOperable.HasValue || request.RepairRequired.HasValue || request.TowingRequired.HasValue ||
                request.ReplacementVehicleRequired.HasValue || request.VehicleCondition is not null || request.DamageDescription is not null ||
                request.EstimatedRepairHours.HasValue || request.DriverSafetyStatus is not null || request.CargoConditionNote is not null)
            .WithMessage("At least one vehicle detail field besides lastUpdatedAt and photoFiles is required.");
    }
}
