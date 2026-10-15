using System.Data;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Pegaxus.Service.RoutePlanService;

public sealed partial class Service
{
    public async Task<Response.ExportResponse> ExportAsync(Guid accountId, Guid routePlanId, string? format, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(format) && !format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
            throw new BadRequestException("Only pdf format is supported");
        var employee = await RequireEmployeeAsync(accountId, cancellationToken);
        var routePlan = await LoadRoutePlanAsync(routePlanId, cancellationToken);
        if (routePlan is null)
            throw new NotFoundException("Route Plan not found", "ROUTE_PLAN_NOT_FOUND");
        if (employee.Role is not (EmployeeRole.LOGISTICS_MANAGER or EmployeeRole.FLEET_ROUTE_COORDINATOR))
            throw new ForbiddenException("Only Logistics Managers and Fleet Route Coordinators can export Route Plans");

        QuestPDF.Settings.License = LicenseType.Community;
        var content = Document.Create(container => container.Page(page =>
        {
            page.Margin(30);
            page.Header().Text($"Route Plan {routePlan.RoutePlanCode}").FontSize(18).Bold();
            page.Content().Column(column =>
            {
                column.Item().Text($"Booking: {routePlan.TransportPlan.Transport.Booking.BookingCode}");
                column.Item().Text($"Type: {GetRouteType(routePlan)} | Status: {routePlan.Status}");
                column.Item().Text($"Note: {routePlan.RouteNote ?? "-"}");
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(35);
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.ConstantColumn(80);
                    });
                    table.Header(header =>
                    {
                        header.Cell().Text("#").Bold();
                        header.Cell().Text("From").Bold();
                        header.Cell().Text("To").Bold();
                        header.Cell().Text("Mode").Bold();
                    });
                    foreach (var item in routePlan.RoutePlanRoutes.OrderBy(item => item.SequenceNo))
                    {
                        table.Cell().Text(item.SequenceNo.ToString());
                        table.Cell().Text(item.Route.FromLocation.LocationName);
                        table.Cell().Text(item.Route.ToLocation.LocationName);
                        table.Cell().Text(item.Route.TransportMode.ToString());
                    }
                });
            });
        })).GeneratePdf();
        return new Response.ExportResponse
        {
            Content = content,
            FileName = $"{routePlan.RoutePlanCode}.pdf",
            ContentType = "application/pdf"
        };
    }
}
