using FluentValidation;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.HandoverService.Validator;

public sealed class CompleteRequestValidator : AbstractValidator<Request.CompleteRequest>
{
    public CompleteRequestValidator()
    {
        RuleFor(request => request.LastUpdatedAt).NotNull().NotEqual(default(DateTimeOffset));
        RuleFor(request => request.Latitude).NotNull().Measurement(Limits.Latitude);
        RuleFor(request => request.Longitude).NotNull().Measurement(Limits.Longitude);
        RuleFor(request => request.HorseConditionNote).NotEmpty().MaximumLength(Limits.NoteMaxLength);
        RuleFor(request => request.DocumentHandoverNote).NotEmpty().MaximumLength(Limits.NoteMaxLength);
        RuleFor(request => request.EquipmentHandoverNote).NotEmpty().MaximumLength(Limits.NoteMaxLength);
        RuleFor(request => request.DiscrepancyNote).MaximumLength(Limits.NoteMaxLength);
        RuleFor(request => request.LocationName).MaximumLength(200);
        RuleFor(request => request.Address).MaximumLength(500);
        RuleFor(request => request.Country).MaximumLength(100);
        RuleFor(request => request.FromPartyName).MaximumLength(200);
        RuleFor(request => request.ToPartyName).MaximumLength(200);
        RuleFor(request => request.FromContactName).MaximumLength(150);
        RuleFor(request => request.ToContactName).MaximumLength(150);
        RuleFor(request => request.FromContactPhone).MaximumLength(30).Must(Phones.IsValid)
            .When(request => !string.IsNullOrWhiteSpace(request.FromContactPhone)).WithMessage(Phones.Message);
        RuleFor(request => request.ToContactPhone).MaximumLength(30).Must(Phones.IsValid)
            .When(request => !string.IsNullOrWhiteSpace(request.ToContactPhone)).WithMessage(Phones.Message);
    }
}
