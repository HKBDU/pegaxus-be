using FluentValidation;

namespace Pegaxus.Service.HandoverService.Validator;

public sealed class CompleteRequestValidator : AbstractValidator<Request.CompleteRequest>
{
    public CompleteRequestValidator()
    {
        RuleFor(request => request.LastUpdatedAt).NotNull().NotEqual(default(DateTimeOffset));
        RuleFor(request => request.HorseConditionNote).NotEmpty();
        RuleFor(request => request.DocumentHandoverNote).NotEmpty();
        RuleFor(request => request.EquipmentHandoverNote).NotEmpty();
        RuleFor(request => request.LocationName).MaximumLength(200);
        RuleFor(request => request.Address).MaximumLength(500);
        RuleFor(request => request.Country).MaximumLength(100);
        RuleFor(request => request.FromPartyName).MaximumLength(200);
        RuleFor(request => request.ToPartyName).MaximumLength(200);
        RuleFor(request => request.FromContactName).MaximumLength(150);
        RuleFor(request => request.ToContactName).MaximumLength(150);
        RuleFor(request => request.FromContactPhone).MaximumLength(30);
        RuleFor(request => request.ToContactPhone).MaximumLength(30);
    }
}
