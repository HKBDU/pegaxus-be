using FluentValidation;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.ClaimService.Validator;

internal static class ClaimRules
{
    public const int NoteMax = 2000;
    public const decimal AmountMax = 9_999_999_999_999_999m;
    public const string WholeVnd = "Amount must be a whole number of VND";

    /// <summary>Length after trimming, the way the contract counts title, description and reasons.</summary>
    public static IRuleBuilderOptions<T, string?> TrimmedBetween<T>(this IRuleBuilder<T, string?> rule, int min, int max, string label) =>
        rule.Must(value => value is not null && value.Trim().Length >= min && value.Trim().Length <= max)
            .WithMessage($"{label} must be between {min} and {max} characters");
}

public sealed class SubmitRequestValidator : AbstractValidator<Request.SubmitRequest>
{
    public SubmitRequestValidator()
    {
        RuleFor(request => request.ClaimType).Must(value => EnumParser.TryParseDefined<ClaimType>(value, out _))
            .WithMessage("Claim type must be HORSE_INJURY, HORSE_HEALTH, LOSS, DAMAGE, DELAY, DOCUMENT_ISSUE, SERVICE_ISSUE or OTHER.");
        RuleFor(request => request.Title).TrimmedBetween(10, 200, "Title");
        RuleFor(request => request.Description).TrimmedBetween(10, ClaimRules.NoteMax, "Description");
        RuleFor(request => request.RequestedCompensationAmount).Must(value => value is null || (value > 0 && value <= ClaimRules.AmountMax))
            .WithMessage("Amount must be greater than 0");
        RuleFor(request => request.RequestedCompensationAmount).Must(value => value is null || Vnd.IsWhole(value.Value))
            .WithMessage(ClaimRules.WholeVnd);
        RuleFor(request => request.EvidenceNote).MaximumLength(ClaimRules.NoteMax);
        RuleFor(request => request.CustomerNote).MaximumLength(ClaimRules.NoteMax);
    }
}

public sealed class ListRequestValidator : AbstractValidator<Request.ListRequest>
{
    public ListRequestValidator()
    {
        RuleFor(request => request.PageIndex).GreaterThan(0);
        RuleFor(request => request.PageSize).InclusiveBetween(1, 100);
        RuleFor(request => request).Must(request => ((long)request.PageIndex - 1) * request.PageSize <= int.MaxValue)
            .WithMessage("The requested page is too large.").OverridePropertyName("pageIndex");
        RuleFor(request => request.ClaimType).Must(value => AllDefined<ClaimType>(value))
            .WithMessage("Claim type must be a comma-separated list of valid claim types.");
        RuleFor(request => request.Status).Must(value => AllDefined<ClaimStatus>(value))
            .WithMessage("Status must be a comma-separated list of PENDING, APPROVED, REJECTED, RESOLVED or CANCELLED.");
        RuleFor(request => request.BookingId).NotEqual(Guid.Empty).When(request => request.BookingId.HasValue);
        RuleFor(request => request.SubmittedTo).Must((request, end) => !end.HasValue || !request.SubmittedFrom.HasValue || end >= request.SubmittedFrom)
            .WithMessage("submittedTo must not be earlier than submittedFrom.");
        RuleFor(request => request.Search).MaximumLength(200);
        RuleFor(request => request.SortBy).Must(sort => new[] { "submittedat:asc", "submittedat:desc", "status:asc", "status:desc",
            "requestedcompensationamount:asc", "requestedcompensationamount:desc" }.Contains(sort?.Trim().ToLowerInvariant()))
            .WithMessage("Sort must use submittedAt, status or requestedCompensationAmount with :asc or :desc.");
    }

    internal static bool AllDefined<T>(string? csv) where T : struct, Enum =>
        string.IsNullOrWhiteSpace(csv) || csv.Split(',').All(part => EnumParser.TryParseDefined<T>(part, out _));
}

public sealed class ResolveRequestValidator : AbstractValidator<Request.ResolveRequest>
{
    public ResolveRequestValidator()
    {
        RuleFor(request => request.LastUpdatedAt).NotNull().NotEqual(default(DateTimeOffset));
        RuleFor(request => request.ResolutionType).Must(value => EnumParser.TryParseDefined<ClaimResolutionType>(value, out _))
            .WithMessage("Resolution type must be COMPENSATION, PARTIAL_COMPENSATION, REFUND, SERVICE_RECOVERY, NO_COMPENSATION or OTHER.");
        RuleFor(request => request.ResolutionSummary).TrimmedBetween(10, ClaimRules.NoteMax, "Resolution summary");
        RuleFor(request => request.CorrectiveAction).MaximumLength(ClaimRules.NoteMax);
        RuleFor(request => request.InternalNote).MaximumLength(ClaimRules.NoteMax);
        RuleFor(request => request.ApprovedCompensationAmount).Must(value => value is null || Vnd.IsWhole(value.Value))
            .WithMessage(ClaimRules.WholeVnd);
        RuleFor(request => request.ApprovedCompensationAmount).Must(value => value is null || (value >= 0 && value <= ClaimRules.AmountMax))
            .WithMessage("Amount must not be negative");
        // R21: the amount must agree with the kind of resolution.
        RuleFor(request => request.ApprovedCompensationAmount).Must((request, amount) =>
        {
            if (!EnumParser.TryParseDefined<ClaimResolutionType>(request.ResolutionType, out var type)) return true;
            return type switch
            {
                ClaimResolutionType.COMPENSATION or ClaimResolutionType.PARTIAL_COMPENSATION or ClaimResolutionType.REFUND => amount > 0,
                ClaimResolutionType.NO_COMPENSATION or ClaimResolutionType.SERVICE_RECOVERY => amount is null or 0,
                _ => true
            };
        }).WithMessage("Approved amount must be greater than 0 for a compensation or refund, and 0 or omitted for no compensation or service recovery.");
    }
}

public sealed class CancelRequestValidator : AbstractValidator<Request.CancelRequest>
{
    public CancelRequestValidator()
    {
        RuleFor(request => request.LastUpdatedAt).NotNull().NotEqual(default(DateTimeOffset));
        RuleFor(request => request.CancellationReason).TrimmedBetween(10, ClaimRules.NoteMax, "Cancellation reason");
    }
}

public sealed class CompleteRequestValidator : AbstractValidator<Request.CompleteRequest>
{
    public CompleteRequestValidator()
    {
        RuleFor(request => request.LastUpdatedAt).NotNull().NotEqual(default(DateTimeOffset));
    }
}
