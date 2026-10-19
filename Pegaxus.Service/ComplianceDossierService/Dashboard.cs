using ClosedXML.Excel;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.ComplianceDossierService;

public sealed partial class Service
{
    public async Task<Response.DashboardResponse> GetDashboardAsync(
        Guid accountId,
        Request.DashboardRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PageIndex < 1 || request.PageSize is < 1 or > 100)
            throw new BadRequestException("PageIndex must be at least 1 and PageSize must be between 1 and 100");

        var role = await _db.Employees
            .Active()
            .Where(employee => employee.AccountId == accountId)
            .Select(employee => (EmployeeRole?)employee.Role)
            .SingleOrDefaultAsync(cancellationToken);
        if (role is not (EmployeeRole.TRANSPORT_SPECIALIST or EmployeeRole.LOGISTICS_MANAGER))
            throw new ForbiddenException("Only Transport Specialists and Logistics Managers can access this dashboard");

        var dossiers = await _db.ComplianceDossiers
            .AsNoTracking()
            .Include(item => item.Transport)
                .ThenInclude(item => item.Booking)
                    .ThenInclude(item => item.Customer)
            .Include(item => item.DossierRequirements)
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dashboardItems = dossiers.Select(item =>
        {
            var requirements = item.DossierRequirements.Where(requirement => !requirement.IsDeleted).ToList();
            var completed = requirements.Count(requirement =>
                requirement.Status is DossierRequirementStatus.SATISFIED or DossierRequirementStatus.WAIVED);
            var progress = requirements.Count == 0
                ? 100m
                : Math.Round(completed * 100m / requirements.Count, 1);
            var isUrgent = item.Transport.Booking.RequestedDepartureDate >= today &&
                item.Transport.Booking.RequestedDepartureDate <= today.AddDays(7) && progress < 100m;

            return new DashboardProjection
            {
                Dossier = item,
                ProgressPercent = progress,
                IsUrgentRisk = isUrgent
            };
        }).ToList();

        var kpi = new Response.DashboardKpiResponse
        {
            TotalActive = dashboardItems.Count(item => item.Dossier.Status is not (ComplianceDossierStatus.APPROVED or ComplianceDossierStatus.CLOSED)),
            ReadyForDeparture = dashboardItems.Count(item => item.Dossier.Status == ComplianceDossierStatus.READY_FOR_SUBMISSION),
            ActionRequired = dashboardItems.Count(item => item.Dossier.Status is ComplianceDossierStatus.REJECTED or ComplianceDossierStatus.IN_PREPARATION),
            SubmittedToAuthorities = dashboardItems.Count(item => item.Dossier.Status is ComplianceDossierStatus.SUBMITTED or ComplianceDossierStatus.PENDING),
            UrgentRisk = dashboardItems.Count(item => item.IsUrgentRisk)
        };

        var filtered = dashboardItems.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!EnumParser.TryParseDefined<ComplianceDossierStatus>(request.Status.Trim(), out var status))
                throw new BadRequestException("Status is invalid", "INVALID_STATUS_PARAMETER");
            filtered = filtered.Where(item => item.Dossier.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(request.Risk))
        {
            var risk = request.Risk.Trim().ToUpperInvariant();
            if (risk is not ("URGENT_RISK" or "NORMAL"))
                throw new BadRequestException("Risk must be URGENT_RISK or NORMAL", "INVALID_RISK_PARAMETER");
            filtered = filtered.Where(item => risk == "URGENT_RISK" ? item.IsUrgentRisk : !item.IsUrgentRisk);
        }

        if (!string.IsNullOrWhiteSpace(request.RouteType))
        {
            var routeType = request.RouteType.Trim().ToUpperInvariant();
            if (routeType is not ("DOMESTIC" or "CROSS_BORDER"))
                throw new BadRequestException("Route type must be DOMESTIC or CROSS_BORDER", "INVALID_ROUTE_TYPE_PARAMETER");
            filtered = filtered.Where(item => GetRouteType(item.Dossier) == routeType);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            filtered = filtered.Where(item =>
                item.Dossier.Transport.Booking.BookingCode.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Dossier.DossierCode.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Dossier.Transport.Booking.Customer.FullName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var sortBy = string.IsNullOrWhiteSpace(request.SortBy)
            ? "requestedDepartureDate:asc"
            : request.SortBy.Trim().ToLowerInvariant();
        filtered = sortBy switch
        {
            "requesteddeparturedate:asc" => filtered.OrderBy(item => item.Dossier.Transport.Booking.RequestedDepartureDate),
            "requesteddeparturedate:desc" => filtered.OrderByDescending(item => item.Dossier.Transport.Booking.RequestedDepartureDate),
            "progresspercent:asc" => filtered.OrderBy(item => item.ProgressPercent),
            _ => throw new BadRequestException("The sort parameter is invalid", "INVALID_SORT_PARAMETER")
        };

        var filteredItems = filtered.ToList();
        var totalItems = filteredItems.Count;
        var totalPages = totalItems == 0 ? 0 : Paging.TotalPages(totalItems, request.PageSize);
        var items = filteredItems
            .Skip(Paging.Offset(request.PageIndex, request.PageSize))
            .Take(request.PageSize)
            .Select(item => new Response.DashboardItemResponse
            {
                ComplianceDossierId = item.Dossier.ComplianceDossierId,
                DossierCode = item.Dossier.DossierCode,
                DossierType = item.Dossier.DossierType.ToString(),
                BookingCode = item.Dossier.Transport.Booking.BookingCode,
                CustomerName = item.Dossier.Transport.Booking.Customer.FullName,
                OriginCountry = item.Dossier.Transport.Booking.OriginCountry,
                DestinationCountry = item.Dossier.Transport.Booking.DestinationCountry,
                RequestedDepartureDate = item.Dossier.Transport.Booking.RequestedDepartureDate,
                ProgressPercent = item.ProgressPercent,
                Status = item.Dossier.Status.ToString(),
                IsUrgentRisk = item.IsUrgentRisk
            }).ToList();

        return new Response.DashboardResponse
        {
            Kpi = kpi,
            Items = items,
            PageIndex = request.PageIndex,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            TotalPages = totalPages
        };
    }

    public async Task<Response.DashboardExportResponse> ExportDashboardAsync(
        Guid accountId,
        Request.DashboardExportRequest request,
        CancellationToken cancellationToken = default)
    {
        var format = request.Format.Trim().ToLowerInvariant();
        if (format is not ("xlsx" or "pdf"))
            throw new BadRequestException("format must be xlsx or pdf", "INVALID_EXPORT_FORMAT");

        var pageRequest = new Request.DashboardRequest
        {
            PageIndex = 1,
            PageSize = 100,
            Status = request.Status,
            Risk = request.Risk,
            RouteType = request.RouteType,
            SortBy = request.SortBy,
            Search = request.Search
        };
        var firstPage = await GetDashboardAsync(accountId, pageRequest, cancellationToken);
        var items = firstPage.Items.ToList();

        for (var pageIndex = 2; pageIndex <= firstPage.TotalPages; pageIndex++)
        {
            pageRequest.PageIndex = pageIndex;
            var page = await GetDashboardAsync(accountId, pageRequest, cancellationToken);
            items.AddRange(page.Items);
        }

        if (format == "xlsx")
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("ComplianceDossiers");
            worksheet.Cell(1, 1).Value = "ComplianceDossier Progress Report";
            worksheet.Cell(2, 1).Value = "Total active";
            worksheet.Cell(2, 2).Value = firstPage.Kpi.TotalActive;
            worksheet.Cell(2, 3).Value = "Urgent risk";
            worksheet.Cell(2, 4).Value = firstPage.Kpi.UrgentRisk;

            var headerRow = 4;
            var headers = new[]
            {
                "ComplianceDossier ID", "Booking Code", "Customer", "Origin Country",
                "Destination Country", "Requested Departure Date", "Progress Percent",
                "Status", "Urgent Risk"
            };
            for (var column = 0; column < headers.Length; column++)
                worksheet.Cell(headerRow, column + 1).Value = headers[column];

            for (var index = 0; index < items.Count; index++)
            {
                var row = headerRow + index + 1;
                var item = items[index];
                worksheet.Cell(row, 1).Value = item.ComplianceDossierId.ToString();
                worksheet.Cell(row, 2).Value = item.BookingCode;
                worksheet.Cell(row, 3).Value = item.CustomerName;
                worksheet.Cell(row, 4).Value = item.OriginCountry;
                worksheet.Cell(row, 5).Value = item.DestinationCountry;
                worksheet.Cell(row, 6).Value = item.RequestedDepartureDate.ToString("yyyy-MM-dd");
                worksheet.Cell(row, 7).Value = item.ProgressPercent;
                worksheet.Cell(row, 8).Value = item.Status;
                worksheet.Cell(row, 9).Value = item.IsUrgentRisk ? "URGENT_RISK" : "NORMAL";
            }

            worksheet.Row(1).Style.Font.Bold = true;
            worksheet.Row(headerRow).Style.Font.Bold = true;
            worksheet.Columns().AdjustToContents();
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return new Response.DashboardExportResponse
            {
                Content = stream.ToArray(),
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                FileName = $"compliance-dossiers-{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx"
            };
        }

        return new Response.DashboardExportResponse
        {
            Content = CreateDashboardPdf(firstPage.Kpi, items),
            ContentType = "application/pdf",
            FileName = $"compliance-dossiers-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf"
        };
    }

    private static byte[] CreateDashboardPdf(
        Response.DashboardKpiResponse kpi,
        IReadOnlyList<Response.DashboardItemResponse> items)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(document => document.Page(page =>
        {
            page.Margin(30);
            page.Header().Text("ComplianceDossier Progress Report")
                .FontSize(18)
                .Bold();
            page.Content().Column(column =>
            {
                column.Spacing(8);
                column.Item().Text($"Total active: {kpi.TotalActive} | Ready: {kpi.ReadyForDeparture} | " +
                    $"Action required: {kpi.ActionRequired} | Submitted: {kpi.SubmittedToAuthorities} | " +
                    $"Urgent risk: {kpi.UrgentRisk}");
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(1.2f);
                        columns.RelativeColumn(1.4f);
                        columns.RelativeColumn(1.7f);
                        columns.RelativeColumn(1.2f);
                        columns.RelativeColumn(1.1f);
                        columns.RelativeColumn(1.5f);
                    });
                    table.Header(header =>
                    {
                        header.Cell().Element(PdfHeaderCell).Text("Booking");
                        header.Cell().Element(PdfHeaderCell).Text("Customer");
                        header.Cell().Element(PdfHeaderCell).Text("Route");
                        header.Cell().Element(PdfHeaderCell).Text("Departure");
                        header.Cell().Element(PdfHeaderCell).Text("Progress");
                        header.Cell().Element(PdfHeaderCell).Text("Status");
                    });

                    foreach (var item in items)
                    {
                        table.Cell().Element(PdfBodyCell).Text(item.BookingCode);
                        table.Cell().Element(PdfBodyCell).Text(item.CustomerName);
                        table.Cell().Element(PdfBodyCell).Text($"{item.OriginCountry} - {item.DestinationCountry}");
                        table.Cell().Element(PdfBodyCell).Text(item.RequestedDepartureDate.ToString("yyyy-MM-dd"));
                        table.Cell().Element(PdfBodyCell).Text($"{item.ProgressPercent:0.0}%");
                        table.Cell().Element(PdfBodyCell).Text(item.Status);
                    }
                });
            });
            page.Footer().AlignCenter().Text(text =>
            {
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        })).GeneratePdf();
    }

    private static IContainer PdfHeaderCell(IContainer container) =>
        container.Background(Colors.Blue.Medium).Padding(4).DefaultTextStyle(x => x.FontColor(Colors.White).Bold());

    private static IContainer PdfBodyCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4);

    private static string GetRouteType(ComplianceDossier dossier) =>
        string.Equals(dossier.Transport.Booking.OriginCountry.Trim(),
            dossier.Transport.Booking.DestinationCountry.Trim(),
            StringComparison.OrdinalIgnoreCase)
            ? "DOMESTIC"
            : "CROSS_BORDER";

    private sealed class DashboardProjection
    {
        public ComplianceDossier Dossier { get; init; } = null!;

        public decimal ProgressPercent { get; init; }

        public bool IsUrgentRisk { get; init; }
    }

    public async Task<Response.AuditHistoryResponse> GetAuditHistoryAsync(
        Guid accountId,
        Guid dossierId,
        Request.AuditHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PageIndex < 1 || request.PageSize is < 1 or > 100)
            throw new BadRequestException("PageIndex must be at least 1 and PageSize must be between 1 and 100");

        var hasAccess = await _db.Employees.Active().AnyAsync(employee => employee.AccountId == accountId && (employee.Role == EmployeeRole.TRANSPORT_SPECIALIST || employee.Role == EmployeeRole.LOGISTICS_MANAGER),
            cancellationToken);

        if (!hasAccess)
            throw new ForbiddenException("Only Transport Specialists and Logistics Managers can access audit history");

        var dossierExists = await _db.ComplianceDossiers
            .AsNoTracking()
            .AnyAsync(dossier => dossier.ComplianceDossierId == dossierId, cancellationToken);
        if (!dossierExists)
            throw new NotFoundException("ComplianceDossier not found", "DOSSIER_NOT_FOUND");

        var query = _db.AuditDossiers
            .AsNoTracking()
            .Where(audit => audit.ComplianceDossierId == dossierId)
            .Include(audit => audit.ActorAccount)
                .ThenInclude(account => account.EmployeeAccount)
            .Include(audit => audit.ActorAccount)
                .ThenInclude(account => account.CustomerAccount)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.ActionType))
        {
            var actionType = request.ActionType.Trim().ToUpperInvariant();
            if (!AuditActionTypes.Contains(actionType))
                throw new BadRequestException("The audit action type is invalid", "INVALID_ACTION_TYPE");

            query = query.Where(audit => audit.ActionType == actionType);
        }

        if (request.From.HasValue)
        {
            var from = Truncate(request.From.Value);
            query = query.Where(audit => audit.CreatedAt >= from);
        }

        if (request.To.HasValue)
        {
            var to = Truncate(request.To.Value);
            query = query.Where(audit => audit.CreatedAt <= to);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(audit =>
                (audit.Summary != null && audit.Summary.ToLower().Contains(search)) ||
                (audit.ActorAccount.EmployeeAccount != null && audit.ActorAccount.EmployeeAccount.FullName.ToLower().Contains(search)) ||
                (audit.ActorAccount.CustomerAccount != null && audit.ActorAccount.CustomerAccount.FullName.ToLower().Contains(search)));
        }

        var sortBy = string.IsNullOrWhiteSpace(request.SortBy)
            ? "occurredat:desc"
            : request.SortBy.Trim().ToLowerInvariant();
        query = sortBy switch
        {
            "occurredat:desc" => query.OrderByDescending(audit => audit.CreatedAt),
            "occurredat:asc" => query.OrderBy(audit => audit.CreatedAt),
            _ => throw new BadRequestException("The sort parameter is invalid", "INVALID_SORT_PARAMETER")
        };

        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : Paging.TotalPages(totalItems, request.PageSize);
        var audits = await query
            .Skip(Paging.Offset(request.PageIndex, request.PageSize))
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new Response.AuditHistoryResponse
        {
            Items = audits.Select(audit => new Response.AuditHistoryItemResponse
            {
                ActionType = audit.ActionType,
                TargetEntity = audit.TargetEntity,
                TargetEntityId = audit.TargetId,
                Actor = new Response.AuditActorResponse
                {
                    AccountId = audit.ActorAccountId,
                    FullName = audit.ActorAccount.EmployeeAccount?.FullName
                        ?? audit.ActorAccount.CustomerAccount?.FullName
                        ?? audit.ActorAccount.Email,
                    Role = audit.ActorAccount.EmployeeAccount?.Role.ToString() ?? "CUSTOMER"
                },
                Note = audit.Summary,
                OccurredAt = AsUtc(audit.CreatedAt)
            }).ToList(),
            PageIndex = request.PageIndex,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            TotalPages = totalPages
        };
    }
}
