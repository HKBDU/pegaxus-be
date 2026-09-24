using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.HorseService;

public sealed class Service : IService
{
    private readonly AppDbContext _appDbContext;

    public Service(AppDbContext appDbContext)
    {
        _appDbContext = appDbContext;
    }

    public async Task<Response.HorseListResponse> GetCustomerHorsesAsync(
        Guid accountId,
        Request.HorseListRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PageIndex < 1)
            throw new BadRequestException("PageIndex must be greater than or equal to 1");
        if (request.PageSize < 1 || request.PageSize > 100)
            throw new BadRequestException("PageSize must be between 1 and 100");

        var customerId = await _appDbContext.Customers
            .Where(customer =>
                customer.AccountId == accountId &&
                !customer.IsDeleted &&
                !customer.Account.IsDeleted)
            .Select(customer => (Guid?)customer.CustomerId)
            .SingleOrDefaultAsync(cancellationToken);

        if (customerId is null)
            throw new ForbiddenException("Only customer accounts can access horses");

        var query = _appDbContext.Horses
            .AsNoTracking()
            .Where(horse =>
                horse.CustomerId == customerId.Value &&
                !horse.IsDeleted);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            query = query.Where(horse =>
                EF.Functions.ILike(horse.HorseName, $"%{search}%") ||
                (horse.MicrochipNumber != null && EF.Functions.ILike(horse.MicrochipNumber, $"%{search}%")) ||
                (horse.PassportNumber != null && EF.Functions.ILike(horse.PassportNumber, $"%{search}%")));
        }

        var totalItems = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(horse => horse.HorseName)
            .ThenBy(horse => horse.HorseId)
            .Skip((request.PageIndex - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(horse => new Response.HorseItemResponse
            {
                HorseId = horse.HorseId,
                HorseName = horse.HorseName,
                Sex = horse.Sex.HasValue ? horse.Sex.Value.ToString() : null,
                Breed = horse.Breed,
                MicrochipNumber = horse.MicrochipNumber
            })
            .ToListAsync(cancellationToken);

        return new Response.HorseListResponse
        {
            Items = items,
            PageIndex = request.PageIndex,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling(totalItems / (double)request.PageSize)
        };
    }

    public async Task<Response.HorseCreatedResponse> CreateHorseAsync(
        Guid accountId,
        Request.CreateHorseRequest request,
        CancellationToken cancellationToken = default)
    {
        var customerId = await _appDbContext.Customers
            .Where(customer =>
                customer.AccountId == accountId && 
                !customer.IsDeleted &&
                !customer.Account.IsDeleted)
            .Select(customer => (Guid?)customer.CustomerId)
            .SingleOrDefaultAsync(cancellationToken);

        if (customerId is null)
            throw new ForbiddenException("Only customer accounts can create horses");

        var horseName = request.HorseName.Trim();
        ValidateText(horseName, 150, "HorseName", required: true);
        ValidateText(request.Breed, 100, "Breed");
        ValidateText(request.Color, 100, "Color");
        ValidateText(request.CountryOfOrigin, 100, "CountryOfOrigin");
        ValidateText(request.MicrochipNumber, 100, "MicrochipNumber");
        ValidateText(request.PassportNumber, 100, "PassportNumber");
        ValidateMeasurement(request.WeightKg, "WeightKg");
        ValidateMeasurement(request.HeightCm, "HeightCm");

        var sex = ParseSex(request.Sex);

        var microchipNumber = NormalizeOptional(request.MicrochipNumber);
        var passportNumber = NormalizeOptional(request.PassportNumber);

        if (microchipNumber is not null && await _appDbContext.Horses.AnyAsync(
                horse => horse.MicrochipNumber == microchipNumber,
                cancellationToken))
            throw new ConflictException(
                "Microchip number already exists",
                "DUPLICATE_MICROCHIP");

        if (passportNumber is not null && await _appDbContext.Horses.AnyAsync(
                horse => horse.PassportNumber == passportNumber,
                cancellationToken))
            throw new ConflictException(
                "Passport number already exists",
                "DUPLICATE_PASSPORT");

        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var horse = new Horse
        {
            HorseId = Guid.NewGuid(),
            CustomerId = customerId.Value,
            HorseName = horseName,
            Sex = sex,
            Breed = NormalizeOptional(request.Breed),
            DateOfBirth = request.DateOfBirth,
            Color = NormalizeOptional(request.Color),
            CountryOfOrigin = NormalizeOptional(request.CountryOfOrigin),
            WeightKg = request.WeightKg,
            HeightCm = request.HeightCm,
            MicrochipNumber = microchipNumber,
            PassportNumber = passportNumber,
            IdentificationNotes = NormalizeOptional(request.IdentificationNotes),
            CreatedAt = now,
            UpdatedAt = now
        };

        _appDbContext.Horses.Add(horse);
        try
        {
            await _appDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException postgresException &&
                  postgresException.ConstraintName is "Horse_microchip_number_key" or "Horse_passport_number_key")
        {
            if (postgresException.ConstraintName == "Horse_microchip_number_key")
                throw new ConflictException(
                    "Microchip number already exists",
                    "DUPLICATE_MICROCHIP");

            throw new ConflictException(
                "Passport number already exists",
                "DUPLICATE_PASSPORT");
        }

        return new Response.HorseCreatedResponse
        {
            HorseId = horse.HorseId,
            HorseName = horse.HorseName
        };
    }

    public async Task<Response.HorseDetailResponse> GetHorseAsync(
        Guid accountId,
        Guid horseId,
        bool canViewAll,
        CancellationToken cancellationToken = default)
    {
        var query = _appDbContext.Horses
            .AsNoTracking()
            .Where(item => item.HorseId == horseId && !item.IsDeleted);

        if (!canViewAll)
        {
            query = query.Where(item =>
                item.Customer.AccountId == accountId &&
                !item.Customer.IsDeleted &&
                !item.Customer.Account.IsDeleted);
        }

        var horse = await query.SingleOrDefaultAsync(cancellationToken);
        if (horse is null)
            throw new NotFoundException("Horse not found", "HORSE_NOT_FOUND");

        return new Response.HorseDetailResponse
        {
            HorseId = horse.HorseId,
            HorseName = horse.HorseName,
            Sex = horse.Sex?.ToString(),
            Breed = horse.Breed,
            DateOfBirth = horse.DateOfBirth,
            Color = horse.Color,
            CountryOfOrigin = horse.CountryOfOrigin,
            WeightKg = horse.WeightKg,
            HeightCm = horse.HeightCm,
            PassportNumber = horse.PassportNumber,
            MicrochipNumber = horse.MicrochipNumber,
            IdentificationNotes = horse.IdentificationNotes,
            CreatedAt = ToUtcOffset(horse.CreatedAt),
            UpdatedAt = ToUtcOffset(horse.UpdatedAt)
        };
    }

    public async Task UpdateHorseAsync(
        Guid accountId,
        Guid horseId,
        Request.CreateHorseRequest request,
        CancellationToken cancellationToken = default)
    {
        var horse = await _appDbContext.Horses
            .SingleOrDefaultAsync(item =>
                item.HorseId == horseId &&
                !item.IsDeleted &&
                item.Customer.AccountId == accountId &&
                !item.Customer.IsDeleted &&
                !item.Customer.Account.IsDeleted,
                cancellationToken);

        if (horse is null)
            throw new NotFoundException("Horse not found", "HORSE_NOT_FOUND");

        var isInApprovedBooking = await _appDbContext.BookingHorses.AnyAsync(item =>
            item.HorseId == horseId &&
            item.Booking.Status == BookingStatus.APPROVED,
            cancellationToken);
        if (isInApprovedBooking)
            throw new ConflictException(
                "Horse belongs to an approved request and cannot be updated",
                "HORSE_IN_APPROVED_BOOKING");

        var horseName = request.HorseName.Trim();
        ValidateText(horseName, 150, "HorseName", required: true);
        ValidateText(request.Breed, 100, "Breed");
        ValidateText(request.Color, 100, "Color");
        ValidateText(request.CountryOfOrigin, 100, "CountryOfOrigin");
        ValidateText(request.MicrochipNumber, 100, "MicrochipNumber");
        ValidateText(request.PassportNumber, 100, "PassportNumber");
        ValidateMeasurement(request.WeightKg, "WeightKg");
        ValidateMeasurement(request.HeightCm, "HeightCm");

        var sex = ParseSex(request.Sex);
        var microchipNumber = NormalizeOptional(request.MicrochipNumber);
        var passportNumber = NormalizeOptional(request.PassportNumber);

        if (microchipNumber is not null && await _appDbContext.Horses.AnyAsync(item =>
                item.HorseId != horseId && item.MicrochipNumber == microchipNumber,
                cancellationToken))
            throw new ConflictException(
                "Microchip number already exists",
                "DUPLICATE_MICROCHIP");

        if (passportNumber is not null && await _appDbContext.Horses.AnyAsync(item =>
                item.HorseId != horseId && item.PassportNumber == passportNumber,
                cancellationToken))
            throw new ConflictException(
                "Passport number already exists",
                "DUPLICATE_PASSPORT");

        horse.HorseName = horseName;
        horse.Sex = sex;
        horse.Breed = NormalizeOptional(request.Breed);
        horse.DateOfBirth = request.DateOfBirth;
        horse.Color = NormalizeOptional(request.Color);
        horse.CountryOfOrigin = NormalizeOptional(request.CountryOfOrigin);
        horse.WeightKg = request.WeightKg;
        horse.HeightCm = request.HeightCm;
        horse.MicrochipNumber = microchipNumber;
        horse.PassportNumber = passportNumber;
        horse.IdentificationNotes = NormalizeOptional(request.IdentificationNotes);
        horse.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        try
        {
            await _appDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException postgresException &&
                  postgresException.ConstraintName is "Horse_microchip_number_key" or "Horse_passport_number_key")
        {
            if (postgresException.ConstraintName == "Horse_microchip_number_key")
                throw new ConflictException(
                    "Microchip number already exists",
                    "DUPLICATE_MICROCHIP");

            throw new ConflictException(
                "Passport number already exists",
                "DUPLICATE_PASSPORT");
        }
    }

    private static void ValidateText(string? value, int maxLength, string fieldName, bool required = false)
    {
        if (required && string.IsNullOrWhiteSpace(value))
            throw new BadRequestException($"{fieldName} is required");
        if (value is not null && value.Length > maxLength)
            throw new BadRequestException($"{fieldName} must not exceed {maxLength} characters");
    }

    private static void ValidateMeasurement(decimal? value, string fieldName)
    {
        if (value is <= 0)
            throw new BadRequestException($"{fieldName} must be greater than 0");
    }

    private static HorseSex? ParseSex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!Enum.TryParse<HorseSex>(value.Trim(), true, out var sex))
            throw new BadRequestException("Sex is invalid");
        return sex;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
