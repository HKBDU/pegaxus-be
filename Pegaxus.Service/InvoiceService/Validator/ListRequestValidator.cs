using FluentValidation;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.InvoiceService.Validator;

public sealed class ListRequestValidator : AbstractValidator<Request.ListRequest>
{
    public ListRequestValidator()
    {
        RuleFor(request => request.PageIndex).GreaterThan(0);
        RuleFor(request => request.PageSize).InclusiveBetween(1, 100);
        RuleFor(request => request).Must(request => ((long)request.PageIndex - 1) * request.PageSize <= int.MaxValue)
            .WithMessage("The requested page is too large.").OverridePropertyName("pageIndex");
        RuleFor(request => request.Type).Must(type => string.IsNullOrWhiteSpace(type) ||
            Enum.GetNames<InvoiceType>().Contains(type.Trim(), StringComparer.OrdinalIgnoreCase)).WithMessage("Invalid invoice type.");
        RuleFor(request => request.Status).Must(status => string.IsNullOrWhiteSpace(status) ||
            new[] { "DRAFT", "ISSUED", "PAID", "OVERDUE", "CANCELLED" }.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Status must be DRAFT, ISSUED, PAID, OVERDUE or CANCELLED.");
        RuleFor(request => request.BookingId).NotEqual(Guid.Empty).When(request => request.BookingId.HasValue);
        RuleFor(request => request.TransportPlanId).NotEqual(Guid.Empty).When(request => request.TransportPlanId.HasValue);
        RuleFor(request => request.IssuedTo).Must((request, end) => !end.HasValue || !request.IssuedFrom.HasValue || end >= request.IssuedFrom)
            .WithMessage("issuedTo must not be earlier than issuedFrom.");
        RuleFor(request => request.DueTo).Must((request, end) => !end.HasValue || !request.DueFrom.HasValue || end >= request.DueFrom)
            .WithMessage("dueTo must not be earlier than dueFrom.");
        RuleFor(request => request.Search).MaximumLength(200);
        RuleFor(request => request.SortBy).Must(sort => new[] { "createdat:asc", "createdat:desc", "issuedat:asc", "issuedat:desc",
            "duedate:asc", "duedate:desc", "totalamount:asc", "totalamount:desc" }.Contains(sort?.Trim().ToLowerInvariant()))
            .WithMessage("Sort must use createdAt, issuedAt, dueDate or totalAmount with :asc or :desc.");
    }
}
