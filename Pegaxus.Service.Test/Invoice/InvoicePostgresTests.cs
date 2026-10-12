using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.InvoiceService.Validator;
using DraftInvoiceService = Pegaxus.Service.InvoiceService.Service;

namespace Pegaxus.Service.Test;

public class InvoicePostgresTests : IAsyncLifetime
{
    private NpgsqlDataSource? _source;

    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("PEGAXUS_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var settings = new NpgsqlConnectionStringBuilder(connection) { Database = $"pegaxus_invoice_test_{Guid.NewGuid():N}" };
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
    public async Task ConcurrentCreation_SameBookingAndTypeCommitsOnlyOneCompleteDraft()
    {
        await using var seed = CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(seed);
        var gate = new InvoiceReadGate();
        await using var first = CreateDb(gate);
        await using var second = CreateDb(gate);
        var attempts = await Task.WhenAll(TryCreate(first), TryCreate(second)).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(1, attempts.Count(error => error is null));
        var conflict = Assert.IsType<PostgresException>(Assert.Single(attempts.Where(error => error is not null))!.GetBaseException());
        Assert.Equal(PostgresErrorCodes.SerializationFailure, conflict.SqlState);
        await using var verification = CreateDb();
        Assert.Single(await verification.Invoices.ToListAsync());
        Assert.Single(await verification.InvoiceItems.ToListAsync());
        Assert.Single(await verification.InvoiceStatusHistories.ToListAsync());

        Task<Exception?> TryCreate(AppDbContext db) => Record.ExceptionAsync(async () =>
        {
            var request = InvoiceTests.Fixture.Request();
            request.Items = [InvoiceTests.Fixture.Item(1.5m, 0.03m)];
            await Service(db).CreateAsync(f.Account.AccountId, f.Booking.BookingId, request);
        });
    }

    [PostgresFact]
    public async Task FailedHistoryInsert_RollsBackInvoiceAndItems()
    {
        await using var db = CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"InvoiceStatusHistory\" ADD CONSTRAINT test_reject_history CHECK (false)");
        var request = InvoiceTests.Fixture.Request();
        request.Items = [InvoiceTests.Fixture.Item(1, 100)];
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, request));
        await using var verification = CreateDb();
        Assert.Empty(await verification.Invoices.ToListAsync());
        Assert.Empty(await verification.InvoiceItems.ToListAsync());
        Assert.Empty(await verification.InvoiceStatusHistories.ToListAsync());
    }

    [PostgresFact]
    public async Task InvoiceCodeCollision_RestoresSavepointAndRetriesWholeGraph()
    {
        await using var seed = CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(seed);
        var existing = f.Invoice(InvoiceType.DEPOSIT, InvoiceStatus.PAID);
        seed.Invoices.Add(existing);
        await seed.SaveChangesAsync();
        var collision = new CodeCollision(existing.InvoiceCode);
        await using var db = CreateDb(collision);
        var request = InvoiceTests.Fixture.Request("FINAL");
        request.Items = [InvoiceTests.Fixture.Item(1, 100)];
        var result = await Service(db).CreateAsync(f.Account.AccountId, f.Booking.BookingId, request);
        Assert.Equal(2, collision.Attempts);
        Assert.NotEqual(existing.InvoiceCode, result.InvoiceNumber);
        await using var verification = CreateDb();
        var invoice = await verification.Invoices.Include(item => item.InvoiceItems).Include(item => item.InvoiceStatusHistories)
            .SingleAsync(item => item.InvoiceId == result.InvoiceId);
        Assert.Equal(2, invoice.InvoiceItems.Count);
        Assert.Single(invoice.InvoiceStatusHistories);
        Assert.Equal(20, invoice.TotalAmount);
    }

    [PostgresFact]
    public async Task InvoiceCodeCollision_StopsAfterTwentyAttemptsAndRollsBack()
    {
        await using var seed = CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(seed);
        var existing = f.Invoice(InvoiceType.DEPOSIT, InvoiceStatus.PAID);
        seed.Invoices.Add(existing);
        await seed.SaveChangesAsync();
        var collision = new CodeCollision(existing.InvoiceCode, 20);
        await using var db = CreateDb(collision);
        var request = InvoiceTests.Fixture.Request();
        request.Items = [InvoiceTests.Fixture.Item(1, 100)];
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(db).CreateAsync(f.Account.AccountId, f.Booking.BookingId, request));
        Assert.Equal("Unable to generate a unique INV code", failure.Message);
        Assert.Equal(20, collision.Attempts);
        await using var verification = CreateDb();
        Assert.Equal(existing.InvoiceId, (await verification.Invoices.SingleAsync()).InvoiceId);
        Assert.Empty(await verification.InvoiceItems.ToListAsync());
        Assert.Empty(await verification.InvoiceStatusHistories.ToListAsync());
    }

    [PostgresFact]
    public async Task PostgreSql_PersistsRoundedMoneyAndUtcResponseTimestamp()
    {
        await using var db = CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        var request = InvoiceTests.Fixture.Request();
        request.Currency = "EUR";
        request.Items = [InvoiceTests.Fixture.Item(1.5m, 0.03m)];
        request.Items[0].HorseId = f.Horse.HorseId;
        var result = await f.Service.CreateAsync(f.Account.AccountId, f.Booking.BookingId, request);
        await using var verification = CreateDb();
        var saved = await verification.Invoices.Include(item => item.InvoiceItems).SingleAsync();
        Assert.Equal(0.05m, saved.Subtotal);
        Assert.Equal(0.01m, saved.TaxAmount);
        Assert.Equal(0.06m, saved.TotalAmount);
        Assert.Equal(result.CreatedAt.UtcDateTime.Ticks, saved.CreatedAt.Ticks);
        Assert.Equal(DateTimeKind.Unspecified, saved.CreatedAt.Kind);
        Assert.Equal(f.Horse.HorseId, Assert.Single(saved.InvoiceItems).HorseId);
    }

    private AppDbContext CreateDb(params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_source!).AddInterceptors(interceptors).Options);
    private static DraftInvoiceService Service(AppDbContext db) => new(db, InvoiceTests.Fixture.Clock,
        new CreateRequestValidator(InvoiceTests.Fixture.Clock), new SuggestionRequestValidator());

    private sealed class EnumNameTranslator : INpgsqlNameTranslator
    {
        private readonly NpgsqlSnakeCaseNameTranslator _snake = new();
        public string TranslateTypeName(string name) => _snake.TranslateTypeName(name);
        public string TranslateMemberName(string name) => name;
    }

    private sealed class InvoiceReadGate : DbCommandInterceptor
    {
        private int _reads;
        private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Invoice\" AS i", StringComparison.Ordinal) &&
                command.CommandText.Contains("ORDER BY", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref _reads) == 2) _bothRead.TrySetResult();
                await _bothRead.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return result;
        }
    }

    private sealed class CodeCollision(string existingCode, int collisions = 1) : SaveChangesInterceptor
    {
        public int Attempts { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (++Attempts <= collisions)
                eventData.Context!.ChangeTracker.Entries<Invoice>().Single(item => item.State == EntityState.Added).Entity.InvoiceCode = existingCode;
            return ValueTask.FromResult(result);
        }
    }
}
