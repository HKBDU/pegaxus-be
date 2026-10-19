using FluentValidation;
using Pegaxus.Service.Common;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.HandoverService.Validator;

public sealed class CreateRequestValidator : AbstractValidator<Request.CreateRequest>
{
    public CreateRequestValidator()
    {
        RuleFor(request => request.HandoverType).Must(value => Enum.GetNames<HandoverType>().Contains(value));
        RuleFor(request => request.PlannedHandoverAt).NotNull().When(request => !request.StartNow);
        RuleFor(request => request.PlannedHandoverAt)
            .Must(value => value is null || value.Value >= DateTimeOffset.UtcNow.AddYears(-Limits.DateWindowYears) && value.Value <= DateTimeOffset.UtcNow.AddYears(Limits.DateWindowYears))
            .WithMessage($"PlannedHandoverAt must be within {Limits.DateWindowYears} years of today.");
        RuleFor(request => request.FromRoutePlanRouteId).NotEqual(Guid.Empty);
        RuleFor(request => request.ToRoutePlanRouteId).NotEqual(Guid.Empty);
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
        When(request => request.HandoverType == "OTHER", () =>
        {
            RuleFor(request => request.FromType).Must(value => value is "CUSTOMER" or "EMPLOYEE" or "PROVIDER");
            RuleFor(request => request.ToType).Must(value => value is "CUSTOMER" or "EMPLOYEE" or "PROVIDER");
            RuleFor(request => request.FromCustomerId).NotEqual(Guid.Empty);
            RuleFor(request => request.FromEmployeeId).NotEqual(Guid.Empty);
            RuleFor(request => request.ToCustomerId).NotEqual(Guid.Empty);
            RuleFor(request => request.ToEmployeeId).NotEqual(Guid.Empty);
        });
    }
}
