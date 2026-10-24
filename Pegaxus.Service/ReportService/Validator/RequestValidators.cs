using FluentValidation;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.ReportService.Validator;

internal static class ReportRules
{
    public const int MaxDays = 366;

    /// <summary>The date range and the filters that RA1 to RA4 all accept.</summary>
    public static void AddFilterRules<T>(this AbstractValidator<T> validator) where T : Request.ReportFilter
    {
        validator.RuleFor(request => request.DateFrom).NotNull().WithMessage("dateFrom is required (YYYY-MM-DD).");
        validator.RuleFor(request => request.DateTo).NotNull().WithMessage("dateTo is required (YYYY-MM-DD).");
        validator.RuleFor(request => request).Must(request => request.DateFrom is not { } from || request.DateTo is not { } to || from <= to)
            .WithMessage("dateFrom must not be later than dateTo.").OverridePropertyName("dateTo");
        validator.RuleFor(request => request).Must(request => request.DateFrom is not { } from || request.DateTo is not { } to || to < from ||
                to.DayNumber - from.DayNumber + 1 <= MaxDays)
            .WithMessage($"The date range must not exceed {MaxDays} days.").OverridePropertyName("dateTo");
        validator.RuleFor(request => request.OriginCountry).MaximumLength(100);
        validator.RuleFor(request => request.DestinationCountry).MaximumLength(100);
        validator.RuleFor(request => request.RouteId).NotEqual(Guid.Empty).When(request => request.RouteId.HasValue);
        validator.RuleFor(request => request.TransportProviderId).NotEqual(Guid.Empty).When(request => request.TransportProviderId.HasValue);
    }

    public static void AddPagingRules<T>(this AbstractValidator<T> validator) where T : Request.PagedFilter
    {
        validator.RuleFor(request => request.PageIndex).GreaterThan(0);
        validator.RuleFor(request => request.PageSize).InclusiveBetween(1, 100);
        validator.RuleFor(request => request).Must(request => ((long)request.PageIndex - 1) * request.PageSize <= int.MaxValue)
            .WithMessage("The requested page is too large.").OverridePropertyName("pageIndex");
    }

    public static IRuleBuilderOptions<T, string?> InSet<T>(this IRuleBuilder<T, string?> rule, string label, params string[] allowed) =>
        rule.Must(value => allowed.Contains(value?.Trim(), StringComparer.OrdinalIgnoreCase)).WithMessage($"{label} must be one of {string.Join(", ", allowed)}.");

    public static bool AllDefined<T>(string? csv) where T : struct, Enum =>
        string.IsNullOrWhiteSpace(csv) || csv.Split(',').All(part => EnumParser.TryParseDefined<T>(part, out _));
}

public sealed class OnTimeRequestValidator : AbstractValidator<Request.OnTimeRequest>
{
    public OnTimeRequestValidator()
    {
        this.AddFilterRules();
        this.AddPagingRules();
        RuleFor(request => request.DriverEmployeeId).NotEqual(Guid.Empty).When(request => request.DriverEmployeeId.HasValue);
        RuleFor(request => request.Bucket).InSet("bucket", "ON_TIME", "MINOR_DELAY", "MAJOR_DELAY").When(request => request.Bucket is not null);
        RuleFor(request => request.TrendBy).InSet("trendBy", "WEEK", "MONTH");
        RuleFor(request => request.SortBy).InSet("sortBy", "delayMinutes:asc", "delayMinutes:desc", "completedAt:asc", "completedAt:desc");
    }
}

public sealed class IncidentClaimRequestValidator : AbstractValidator<Request.IncidentClaimRequest>
{
    public IncidentClaimRequestValidator()
    {
        this.AddFilterRules();
        this.AddPagingRules();
        RuleFor(request => request.IncidentCategory).Must(ReportRules.AllDefined<IncidentCategory>)
            .WithMessage("incidentCategory must be a comma-separated list of VEHICLE, HORSE_HEALTH, BOTH or ROUTE_CHANGE.");
        RuleFor(request => request.Severity).Must(ReportRules.AllDefined<IncidentSeverity>)
            .WithMessage("severity must be a comma-separated list of LOW, MEDIUM, HIGH or CRITICAL.");
        RuleFor(request => request.ClaimType).Must(ReportRules.AllDefined<ClaimType>)
            .WithMessage("claimType must be a comma-separated list of valid claim types.");
        RuleFor(request => request.VehicleId).NotEqual(Guid.Empty).When(request => request.VehicleId.HasValue);
        RuleFor(request => request.HorseId).NotEqual(Guid.Empty).When(request => request.HorseId.HasValue);
        RuleFor(request => request.SortBy).InSet("sortBy", "occurredAt:asc", "occurredAt:desc");
    }
}

public sealed class TransitTimeRequestValidator : AbstractValidator<Request.TransitTimeRequest>
{
    public TransitTimeRequestValidator()
    {
        this.AddFilterRules();
        this.AddPagingRules();
        RuleFor(request => request.DelayFilter).InSet("delayFilter", "LATE_OVER_1_DAY", "EARLY").When(request => request.DelayFilter is not null);
        RuleFor(request => request.SortBy).InSet("sortBy", "actualDays:asc", "actualDays:desc", "deltaDays:asc", "deltaDays:desc");
    }
}

public sealed class ExportRequestValidator : AbstractValidator<Request.ExportRequest>
{
    public ExportRequestValidator() =>
        RuleFor(request => request.Format).InSet("format", "xlsx", "pdf");
}
