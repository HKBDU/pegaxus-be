using System.Globalization;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Pegaxus.Service.ReportService;

/// <summary>Writes a report as a spreadsheet or a PDF from the same flat list of tables, so both files show exactly the same numbers.</summary>
internal static class ReportFiles
{
    public const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string PdfType = "application/pdf";

    internal sealed record ReportTable(string Title, string[] Headers, IReadOnlyList<object?[]> Rows);

    internal sealed record ReportDocument(string Title, string Subtitle, IReadOnlyList<ReportTable> Tables);

    public static string Show(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";

    private static string Display(object? value) => value switch
    {
        null => "",
        bool flag => flag ? "Yes" : "No",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };

    public static byte[] Xlsx(ReportDocument document)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Report");
        sheet.Cell(1, 1).Value = document.Title;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;
        sheet.Cell(2, 1).Value = document.Subtitle;
        var row = 4;
        foreach (var table in document.Tables)
        {
            sheet.Cell(row, 1).Value = table.Title;
            sheet.Cell(row++, 1).Style.Font.Bold = true;
            for (var column = 0; column < table.Headers.Length; column++)
            {
                var header = sheet.Cell(row, column + 1);
                header.Value = table.Headers[column];
                header.Style.Font.Bold = true;
                header.Style.Fill.BackgroundColor = XLColor.LightGray;
            }
            row++;
            foreach (var values in table.Rows)
            {
                for (var column = 0; column < values.Length; column++)
                {
                    var cell = sheet.Cell(row, column + 1);
                    switch (values[column])
                    {
                        case null: break;
                        case int number: cell.Value = number; break;
                        case long number: cell.Value = number; break;
                        case decimal number: cell.Value = number; break;
                        default: cell.Value = Display(values[column]); break;
                    }
                }
                row++;
            }
            row++;
        }
        sheet.Columns().AdjustToContents(4, row);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] Pdf(ReportDocument document)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;
        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(24);
            page.DefaultTextStyle(style => style.FontSize(8));
            page.Header().Column(column =>
            {
                column.Item().Text(document.Title).FontSize(16).Bold();
                column.Item().Text(document.Subtitle).FontSize(9);
            });
            page.Content().PaddingVertical(8).Column(column =>
            {
                column.Spacing(10);
                foreach (var table in document.Tables)
                {
                    column.Item().Text(table.Title).FontSize(11).Bold();
                    if (table.Rows.Count == 0) { column.Item().Text("No rows"); continue; }
                    column.Item().Table(grid =>
                    {
                        grid.ColumnsDefinition(columns => { foreach (var _ in table.Headers) columns.RelativeColumn(); });
                        grid.Header(header => { foreach (var title in table.Headers) header.Cell().Element(CellStyle).Text(title).Bold(); });
                        foreach (var values in table.Rows)
                            foreach (var value in values)
                                grid.Cell().Element(CellStyle).Text(Display(value));
                    });
                }
            });
            page.Footer().AlignCenter().Text(text =>
            {
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        })).GeneratePdf();
    }

    private static IContainer CellStyle(IContainer container) =>
        container.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(3);
}
