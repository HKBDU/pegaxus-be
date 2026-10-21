using FluentValidation;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.IncidentService.Validator;

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
