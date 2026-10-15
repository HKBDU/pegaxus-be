using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.InvoiceService.Validator;

using DraftInvoiceService = Pegaxus.Service.InvoiceService.Service;

namespace Pegaxus.Service.Test;

[Collection("PostgresSharedDatabase")]
public class InvoiceUpdatePostgresTests : IAsyncLifetime
{
    private NpgsqlDataSource? _source;

    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var settings = new NpgsqlConnectionStringBuilder(connection) { Database = $"pegaxus_invoice_update_test_{Guid.NewGuid():N}" };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString);
        var translator = new EnumNameTranslator();
        foreach (var type in typeof(AccountStatus).Assembly.GetTypes()
                     .Where(type => type.IsEnum && type.Namespace == typeof(AccountStatus).Namespace))
            builder.MapEnum(type, translator.TranslateTypeName(type.Name), translator);
        _source = builder.Build();
        await using var db = CreateDb();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_source is null) return;
        try
        {
            await using var db = CreateDb();
            await db.Database.EnsureDeletedAsync();
        }
        finally { await _source.DisposeAsync(); }
    }

    [PostgresFact]
    public async Task ConcurrentUpdates_SameVersionOnlyOneReplacementCommits()
    {
        await using var seed = CreateDb();
        var f = await InvoiceUpdateTests.Fixture.CreateAsync(seed);
        var keep = f.Invoice.InvoiceItems.First();
        var removed = f.Invoice.InvoiceItems.Last();
        var gate = new InvoiceReadGate(2);
        await using var first = CreateDb(gate);
        await using var second = CreateDb(gate);
        var attempts = await Task.WhenAll(TryUpdate(first, "First"), TryUpdate(second, "Second")).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(1, attempts.Count(error => error is null));
        Assert.Equal("INVOICE_MODIFIED", Assert.IsType<ConflictException>(Assert.Single(attempts.Where(error => error is not null))).ErrorCode);
        await using var verification = CreateDb();
        var saved = await verification.Invoices.Include(item => item.InvoiceItems).SingleAsync();
        Assert.Contains(saved.BillingNote, new[] { "First", "Second" });
        Assert.Equal(2, saved.InvoiceItems.Count);
        Assert.All(saved.InvoiceItems, item => Assert.Equal(saved.BillingNote, item.Note));
        Assert.Contains(saved.InvoiceItems, item => item.InvoiceItemId == keep.InvoiceItemId && item.CreatedAt == keep.CreatedAt);
        Assert.DoesNotContain(saved.InvoiceItems, item => item.InvoiceItemId == removed.InvoiceItemId);
        Assert.Equal(1.32m, saved.TotalAmount);
        Assert.Single(await verification.InvoiceStatusHistories.ToListAsync());

        Task<Exception?> TryUpdate(AppDbContext db, string note) => Record.ExceptionAsync(async () =>
        {
            var request = f.Request();
            request.Note = note;
            request.Items = [InvoiceUpdateTests.Fixture.Item(1, 1, keep.InvoiceItemId), InvoiceUpdateTests.Fixture.Item(1, 0.10m)];
            request.Items.ForEach(item => item.Note = note);
            await Service(db).UpdateAsync(f.Account.AccountId, f.Invoice.InvoiceId, request);
        });
    }

    [PostgresFact]
    public async Task ConcurrentIssue_EditCannotOverwriteStatusOrReplaceIssuedItems()
    {
        await using var seed = CreateDb();
        var f = await InvoiceUpdateTests.Fixture.CreateAsync(seed);
        var gate = new InvoiceReadGate(1);
        await using var editing = CreateDb(gate);
        var request = f.Request();
        request.Items = [InvoiceUpdateTests.Fixture.Item(1, 999)];
        var update = Service(editing).UpdateAsync(f.Account.AccountId, f.Invoice.InvoiceId, request);
        try
        {
            await gate.Read.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var issuing = CreateDb();
            var issued = await issuing.Invoices.SingleAsync();
            issued.Status = InvoiceStatus.ISSUED;
            issued.IssuedAt = DateTime.SpecifyKind(InvoiceUpdateTests.Fixture.Clock.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);
            await issuing.SaveChangesAsync();
        }
        finally { gate.Resume.TrySetResult(); }
        var error = await Assert.ThrowsAsync<ConflictException>(() => update);
        Assert.Equal("INVOICE_MODIFIED", error.ErrorCode);
        await using var verification = CreateDb();
        var saved = await verification.Invoices.Include(item => item.InvoiceItems).SingleAsync();
        Assert.Equal(InvoiceStatus.ISSUED, saved.Status);
        Assert.Equal(120, saved.TotalAmount);
        Assert.Equal(2, saved.InvoiceItems.Count);
    }

    [PostgresFact]
    public async Task FailedNewItemInsert_RollsBackHeaderEditedRowsAndDeletions()
    {
        await using var db = CreateDb();
        var f = await InvoiceUpdateTests.Fixture.CreateAsync(db);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"InvoiceItem\" ADD CONSTRAINT test_reject_item CHECK (note IS DISTINCT FROM 'reject')");
        var request = f.Request();
        request.Note = "Changed header";
        request.Items = [InvoiceUpdateTests.Fixture.Item(1, 999, f.Invoice.InvoiceItems.First().InvoiceItemId), InvoiceUpdateTests.Fixture.Item(1, 1)];
        request.Items[1].Note = "reject";
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Service.UpdateAsync(f.Account.AccountId, f.Invoice.InvoiceId, request));
        await using var verification = CreateDb();
        var saved = await verification.Invoices.Include(item => item.InvoiceItems).SingleAsync();
        Assert.Null(saved.BillingNote);
        Assert.Equal(f.Invoice.UpdatedAt, saved.UpdatedAt);
        Assert.Equal(120, saved.TotalAmount);
        Assert.Equal(2, saved.InvoiceItems.Count);
        Assert.Equal(new decimal[] { 40, 60 }, saved.InvoiceItems.Select(item => item.LineAmount).Order().ToArray());
        Assert.Single(await verification.InvoiceStatusHistories.ToListAsync());
    }

    [PostgresFact]
    public async Task Final_ReplacementPersistsCurrentDeductionsAndRoundTripsNewVersion()
    {
        await using var db = CreateDb();
        var f = await InvoiceUpdateTests.Fixture.CreateAsync(db, InvoiceType.FINAL);
        var deposit = f.Deposit(30);
        db.Invoices.Add(deposit); await db.SaveChangesAsync();
        var request = f.Request();
        request.Items = [InvoiceUpdateTests.Fixture.Item(1, 100, f.Invoice.InvoiceItems.First().InvoiceItemId)];
        request.Items[0].HorseId = f.Horse.HorseId;
        var result = await f.Service.UpdateAsync(f.Account.AccountId, f.Invoice.InvoiceId, request);
        await using var verification = CreateDb();
        var saved = await verification.Invoices.Include(item => item.InvoiceItems).SingleAsync(item => item.InvoiceId == f.Invoice.InvoiceId);
        Assert.Equal(90, saved.TotalAmount);
        Assert.Equal(2, saved.InvoiceItems.Count);
        Assert.Equal(result.UpdatedAt.UtcTicks, saved.UpdatedAt.Ticks);
        Assert.Equal(DateTimeKind.Unspecified, saved.UpdatedAt.Kind);
        Assert.Single(saved.InvoiceItems.Where(item => item.LineAmount == -30 && item.UnitPrice == -30 && item.Quantity == 1));
        request.LastUpdatedAt = result.UpdatedAt;
        var second = await Service(verification).UpdateAsync(f.Account.AccountId, f.Invoice.InvoiceId, request);
        Assert.Equal(result.UpdatedAt.UtcTicks + 10, second.UpdatedAt.UtcTicks);
        Assert.Equal(90, second.Summary.GrandTotal);
        Assert.Single(await verification.InvoiceStatusHistories.ToListAsync());
    }

    private AppDbContext CreateDb(params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseTestNpgsql(_source!).AddInterceptors(interceptors).Options);
    private static DraftInvoiceService Service(AppDbContext db) => new(db,
        new InvoiceOnlinePaymentTests.Gateway(), InvoiceOnlinePaymentTests.Logger, InvoiceUpdateTests.Fixture.Clock,
        new CreateRequestValidator(InvoiceUpdateTests.Fixture.Clock), new SuggestionRequestValidator(), new UpdateRequestValidator(InvoiceUpdateTests.Fixture.Clock),
        new ListRequestValidator(), new BookingListRequestValidator(), new IssueRequestValidator(), new RecordPaymentRequestValidator(InvoiceUpdateTests.Fixture.Clock), new CancelRequestValidator(),
        new NotificationService.Service(db, new NotificationService.NoopNotificationRealtimeService()));

    private sealed class EnumNameTranslator : INpgsqlNameTranslator
    {
        private readonly NpgsqlSnakeCaseNameTranslator _snake = new();
        public string TranslateTypeName(string name) => _snake.TranslateTypeName(name);
        public string TranslateMemberName(string name) => name;
    }

    private sealed class InvoiceReadGate(int readers) : DbCommandInterceptor
    {
        private int _reads;
        public TaskCompletionSource Read { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"InvoiceItem\"", StringComparison.Ordinal) && command.CommandText.Contains("LIMIT 2", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref _reads) == readers)
                {
                    Read.TrySetResult();
                    if (readers > 1) Resume.TrySetResult();
                }
                await Resume.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return result;
        }
    }
}
