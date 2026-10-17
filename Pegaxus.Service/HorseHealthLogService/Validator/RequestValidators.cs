using FluentValidation;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.HorseHealthLogService.Validator;

public sealed class RecordRequestValidator : AbstractValidator<Request.RecordRequest>
{
    public RecordRequestValidator(TimeProvider clock)
    {
        RuleFor(request => request.LogType).Must(value => Enum.GetNames<HorseHealthLogType>().Contains(value))
            .WithMessage("A valid log type is required.");
        RuleFor(request => request.CheckedAt).Must(value => value is null || value <= clock.GetUtcNow().AddMinutes(5))
            .WithMessage("Checked at must not be more than 5 minutes in the future.");
        RuleFor(request => request.LocationDescription).MaximumLength(500);
        RuleFor(request => request.GeneralNote).MaximumLength(2000);
        RuleFor(request => request.Files).Must(files => files is { Count: <= 5 })
            .WithMessage("A health log may have at most 5 photos.");
    }
}

public sealed class AddPhotosRequestValidator : AbstractValidator<Request.AddPhotosRequest>
{
    public AddPhotosRequestValidator()
    {
        RuleFor(request => request.Files).Must(files => files is { Count: >= 1 and <= 5 })
            .WithMessage("Between 1 and 5 photos are required.");
    }
}

public sealed class CreateRequestValidator : AbstractValidator<Request.CreateRequest>
{
    public CreateRequestValidator(IValidator<Request.RecordRequest> recordValidator)
    {
        Include(recordValidator);
        RuleFor(request => request.HorseId).NotEmpty();
        RuleFor(request => request.HealthStatus).Must(value => Enum.GetNames<HorseHealthStatus>().Contains(value))
            .WithMessage("A valid health status is required.");
        RuleFor(request => request.HydrationStatus).Must(value => Enum.GetNames<HydrationStatus>().Contains(value))
            .WithMessage("A valid hydration status is required.");
        RuleFor(request => request.TemperatureCelsius).Must(value => value is null || decimal.Round(value.Value, 1) == value)
            .WithMessage("Temperature must have at most one decimal place.");
        RuleFor(request => request.BehaviorNote).MaximumLength(2000);
        RuleFor(request => request.FeedingNote).MaximumLength(2000);
        RuleFor(request => request.HydrationNote).MaximumLength(2000);
        RuleFor(request => request.SymptomNote).MaximumLength(2000);
        RuleFor(request => request.InjuryNote).MaximumLength(2000);
        RuleFor(request => request.TreatmentProvided).MaximumLength(2000);
        RuleFor(request => request.MedicationGiven).MaximumLength(2000);
        RuleFor(request => request.VeterinarianNote).MaximumLength(2000).NotEmpty().When(request => request.VeterinarianRequired);
        RuleFor(request => request.VeterinarianNote).Null().When(request => !request.VeterinarianRequired)
            .WithMessage("Veterinarian note is only accepted when a veterinarian is required.");
    }
}
