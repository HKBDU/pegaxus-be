using System.Text.Json;
using Pegaxus.Service.Exceptions;

using DossierRequest = Pegaxus.Service.ComplianceDossierService.Request;
using DossierResponse = Pegaxus.Service.ComplianceDossierService.Response;

namespace Pegaxus.Service.Assistant.Tools;

public sealed class CustomerGetDossierTool(Pegaxus.Service.ComplianceDossierService.IComplianceDossierService dossiers) : IAssistantTool
{
    private const int PageSize = 100;
    private const int MaxPages = 100;

    private sealed class Arguments
    {
        public Guid BookingId { get; init; }
    }

    public string Name => "customer_get_dossier";
    public string Description => "Get a booking's compliance dossier and every incomplete requirement for the authenticated customer.";
    public JsonElement ParametersSchema => AssistantToolJson.Schema("""
        {"type":"object","properties":{"bookingId":{"type":"string","format":"uuid"}},"required":["bookingId"],"additionalProperties":false}
        """);

    public async Task<AssistantToolResult> RunAsync(JsonElement arguments, AssistantToolContext context)
    {
        AssistantToolJson.ValidateContext(context);
        var input = AssistantToolJson.Parse<Arguments>(arguments);
        if (input.BookingId == Guid.Empty)
            throw new BadRequestException("bookingId must be a non-empty UUID.", "VALIDATION_ERROR");

        var overview = await dossiers.GetByBookingAsync(context.AccountId, input.BookingId,
            new DossierRequest.ViewRequest(), context.CancellationToken);
        if (overview.ComplianceDossierId is not { } dossierId)
        {
            var notInitialized = new DossierToolData(overview.BookingId, overview.BookingCode, false,
                overview.Message, null, null, overview.FulfilledCount, overview.RequirementCount,
                overview.CompletionPercent, true, 0, 0, null, []);
            var bookingReference = new AssistantReferenceCandidate("booking", overview.BookingId.ToString(), overview.BookingCode, null);
            return new(Name, notInitialized, [bookingReference]);
        }

        var requirements = new List<DossierResponse.RequirementListItemResponse>();
        var totalItems = 0;
        var reportedPages = 0;
        var requestedPages = 0;
        var changedWhilePaging = false;
        do
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            requestedPages++;
            var page = await dossiers.GetRequirementsAsync(context.AccountId, dossierId,
                new DossierRequest.RequirementListRequest { PageIndex = requestedPages, PageSize = PageSize },
                context.CancellationToken);
            if (requestedPages == 1)
            {
                totalItems = page.TotalItems;
                reportedPages = page.TotalPages;
            }
            else if (page.TotalItems != totalItems || page.TotalPages != reportedPages)
            {
                changedWhilePaging = true;
                reportedPages = Math.Max(reportedPages, page.TotalPages);
            }

            requirements.AddRange(page.Items);
        }
        while (requestedPages < Math.Min(reportedPages, MaxPages));

        var complete = requestedPages >= reportedPages && !changedWhilePaging;
        var incompleteRequirements = requirements
            .Where(item => item.Status is not ("SATISFIED" or "WAIVED"))
            .Select(item => new IncompleteDossierRequirement(
                item.DossierRequirementId, item.RequirementName, item.HorseName, item.Status,
                item.IsMandatory, item.DueDate, item.ValidUntil,
                item.Document is null ? null : new DossierDocumentSummary(
                    item.Document.DocumentCode, item.Document.Status, item.Document.LatestVersionNo)))
            .ToArray();
        var data = new DossierToolData(overview.BookingId, overview.BookingCode, true, null,
            dossierId, overview.Status, overview.FulfilledCount, overview.RequirementCount,
            overview.CompletionPercent, complete, requestedPages, totalItems,
            !complete && changedWhilePaging
                ? "Requirement totals changed while paging; the returned requirements may be incomplete."
                : !complete
                    ? $"Only the first {MaxPages} pages were read; more requirements remain."
                    : null,
            incompleteRequirements);
        var reference = new AssistantReferenceCandidate(
            "dossier", dossierId.ToString(), overview.DossierCode ?? "Compliance dossier", null);
        return new(Name, data, [reference]);
    }
}

public sealed record DossierToolData(Guid BookingId, string BookingCode, bool IsInitialized, string? Message,
    Guid? DossierId, string? Status, int FulfilledCount, int RequirementCount, decimal CompletionPercent,
    bool RequirementsComplete, int PagesRead, int TotalRequirements,
    string? ScopeMessage,
    IReadOnlyList<IncompleteDossierRequirement> IncompleteRequirements);

public sealed record IncompleteDossierRequirement(Guid RequirementId, string Name, string? HorseName,
    string Status, bool IsMandatory, DateOnly? DueDate, DateOnly? ValidUntil, DossierDocumentSummary? Document);

public sealed record DossierDocumentSummary(string DocumentCode, string Status, int? LatestVersionNo);
