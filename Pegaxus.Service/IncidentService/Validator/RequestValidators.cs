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

public sealed class HorseDetailValidator : AbstractValidator<Request.HorseDetail>
{
    public HorseDetailValidator(TimeProvider clock)
    {
        RuleFor(item => item.HorseId).NotEmpty();
        RuleFor(item => item.IncidentType).Must(value => Enum.GetNames<HorseIncidentType>().Contains(value))
            .WithMessage("A valid horse incident type is required.");
        RuleFor(item => item.ConditionDescription).Must(value => value?.Trim().Length >= 10).MaximumLength(2000)
            .WithMessage("Condition description must contain between 10 and 2000 characters after trimming.");
        RuleFor(item => item.Symptoms).MaximumLength(2000);
        RuleFor(item => item.FirstAidProvided).MaximumLength(2000);
        RuleFor(item => item.VeterinarianInstruction).MaximumLength(2000);
        RuleFor(item => item.MedicationGiven).MaximumLength(2000);
        RuleFor(item => item.BodyTemperatureCelsius).Must(value => value is null || decimal.Round(value.Value, 1) == value)
            .WithMessage("Body temperature must have at most one decimal place.");
        RuleFor(item => item.VeterinarianContactedAt).Must(value => value is null || value <= clock.GetUtcNow().AddMinutes(5))
            .WithMessage("Veterinarian contact time must not be more than 5 minutes in the future.");
    }
}

public sealed class AddHorseDetailsRequestValidator : AbstractValidator<Request.AddHorseDetailsRequest>
{
    public AddHorseDetailsRequestValidator(IValidator<Request.HorseDetail> horseValidator)
    {
        RuleFor(item => item.Horses).NotEmpty();
        RuleFor(item => item.Horses).Must(horses => horses is null || horses.All(horse => horse is not null) &&
                horses.Select(horse => horse.HorseId).Distinct().Count() == horses.Count)
            .WithMessage("Each horse must be present only once.");
        RuleForEach(item => item.Horses).SetValidator(horseValidator);
        RuleFor(item => item.PhotoFiles).Must(files => files is { Count: <= 5 } && files.All(file => file is not null &&
                !string.IsNullOrWhiteSpace(file.PublicId) && file.PublicId.Length <= 255) &&
                files.Select(file => file.PublicId).Distinct(StringComparer.Ordinal).Count() == files.Count)
            .WithMessage("At most 5 distinct photos with non-empty publicIds of at most 255 characters are accepted.");
    }
}

public sealed class ReportHorseHealthRequestValidator : AbstractValidator<Request.ReportHorseHealthRequest>
{
    public ReportHorseHealthRequestValidator(TimeProvider clock, IValidator<Request.AddHorseDetailsRequest> detailsValidator)
    {
        Include(detailsValidator);
        RuleFor(item => item.TransportId).NotEmpty();
        RuleFor(item => item.Severity).Must(value => Enum.GetNames<IncidentSeverity>().Contains(value))
            .WithMessage("A valid incident severity is required.");
        RuleFor(item => item.Title).MaximumLength(200);
        RuleFor(item => item.ImmediateAction).MaximumLength(2000);
        RuleFor(item => item.OccurredAt).Must(value => value is null || value <= clock.GetUtcNow().AddMinutes(5))
            .WithMessage("Occurred at must not be more than 5 minutes in the future.");
        RuleFor(item => item.LocationId).NotEqual(Guid.Empty).When(item => item.LocationId.HasValue);
        RuleFor(item => item.LocationId).Must((item, id) => id.HasValue || item.Latitude.HasValue && item.Longitude.HasValue)
            .WithMessage("LocationId or both latitude and longitude are required.");
        RuleFor(item => item.Latitude).Must((item, latitude) => latitude is null && item.Longitude is null ||
                latitude.HasValue && item.Longitude.HasValue && Coordinates.IsValid(latitude.Value, item.Longitude.Value))
            .WithMessage("Coordinates must be a valid WGS84 pair with at most four decimal places and cannot be (0, 0).");
        RuleFor(item => item.LocationDescription).MaximumLength(500);
        RuleFor(item => item.Country).MaximumLength(100);
    }
}

public sealed class FollowUpHorseRequestValidator : AbstractValidator<Request.FollowUpHorseRequest>
{
    public FollowUpHorseRequestValidator(TimeProvider clock)
    {
        RuleFor(item => item.LastUpdatedAt).NotNull();
        RuleFor(item => item.OutcomeNote).MaximumLength(2000);
        RuleFor(item => item.MedicationGiven).MaximumLength(2000);
        RuleFor(item => item.VeterinarianInstruction).MaximumLength(2000);
        RuleFor(item => item.VeterinarianContactedAt).Must(value => value is null || value <= clock.GetUtcNow().AddMinutes(5))
            .WithMessage("Veterinarian contact time must not be more than 5 minutes in the future.");
        RuleFor(item => item).Must(item => !string.IsNullOrWhiteSpace(item.OutcomeNote) || !string.IsNullOrWhiteSpace(item.MedicationGiven) ||
                !string.IsNullOrWhiteSpace(item.VeterinarianInstruction) || item.VeterinarianContactedAt.HasValue || item.HasHorseTransportable)
            .WithMessage("At least one follow-up field besides lastUpdatedAt is required.");
    }
}
