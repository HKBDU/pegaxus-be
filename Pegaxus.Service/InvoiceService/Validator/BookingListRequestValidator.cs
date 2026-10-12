using FluentValidation;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.InvoiceService.Validator;

public sealed class BookingListRequestValidator : AbstractValidator<Request.BookingListRequest>
{
    public BookingListRequestValidator()
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
        RuleFor(request => request.SortBy).Must(sort => new[] { "createdat:asc", "createdat:desc", "issuedat:asc", "issuedat:desc",
            "duedate:asc", "duedate:desc", "totalamount:asc", "totalamount:desc" }.Contains(sort?.Trim().ToLowerInvariant()))
            .WithMessage("Sort must use createdAt, issuedAt, dueDate or totalAmount with :asc or :desc.");
    }
}
