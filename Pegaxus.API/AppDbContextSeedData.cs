using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

namespace Pegaxus.API;

public static class AppDbContextSeedData
{
    private const string DefaultPassword = "pegaxusDemo@123";
    private static readonly DateTime ComplianceRequirementSeedTimestamp =
        new(2026, 10, 4, 0, 0, 0, DateTimeKind.Unspecified);

    public static async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var passwordHasher = new PasswordHasher<Account>();
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        var employees = new[]
        {
            new EmployeeSeed("Văng Khánh Khuyên", "EMP-20260927-0001", "diechahien@gmail.com", "0702946163", EmployeeRole.LOGISTICS_MANAGER),
            new EmployeeSeed("Võ Gia Huy", "EMP-20260927-0002", "huyvog6226@gmail.com", "0961002445", EmployeeRole.TRANSPORT_SPECIALIST),
            new EmployeeSeed("Nguyễn Huỳnh Hoàng Uyên", "EMP-20260927-0003", "hoanguyen91011@gmail.com", "0937489883", EmployeeRole.FLEET_ROUTE_COORDINATOR),
            new EmployeeSeed("Ngô Hoàng Thái Dương", "EMP-20260927-0004", "duongngo2209@gmail.com", "0949301121", EmployeeRole.DRIVER),
            new EmployeeSeed("Nguyễn Văn Quốc Bảo", "EMP-20260927-0005", "quocbao.nguyen16102006@gmail.com", "0399556929", EmployeeRole.ESCORT)
        };

        var customers = new[]
        {
            new CustomerSeed("Trinh Ng An Quan", "trinhnganquan@gmail.com"),
            new CustomerSeed("Bat Quy Quan", "batquyquan@gmail.com"),
            new CustomerSeed("Kim Ve Tuan", "kimvetuan@gmail.com"),
            new CustomerSeed("Khuyen Vkse200481", "khuyenvkse200481@fpt.edu.vn")
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var seed in employees)
            await SeedEmployeeAsync(db, passwordHasher, seed, now, cancellationToken);
        foreach (var seed in customers)
            await SeedCustomerAsync(db, passwordHasher, seed, now, cancellationToken);
        await SeedComplianceRequirementsAsync(db, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await SeedDemoOperationsAsync(db, now, cancellationToken);
        await SeedLocationsAsync(db, cancellationToken);
        await SeedVehiclesAndProvidersAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // Rows still carrying the old placeholder names are upgraded once; an edited row no longer matches and is left alone.
    private const string LegacyDemoVehiclePrefix = "Pegaxus Demo ";
    private const string LegacyDemoProviderPrefix = "Pegaxus Demo Provider";

    public static async Task<(int Vehicles, int Providers)> SeedVehiclesAndProvidersAsync(
        AppDbContext db, CancellationToken cancellationToken = default)
    {
        var now = DbTime.UtcNow();
        var expiryDate = DateOnly.FromDateTime(now).AddYears(2);
        var vehicles = 0;
        var providers = 0;
        foreach (var number in Enumerable.Range(1, 10))
        {
            var vehicleProfile = VehicleProfiles[number - 1];
            var vehicleCode = $"VEH-20261010-{number:0000}";
            var vehicleId = StableGuid(vehicleCode);
            var licensePlate = vehicleProfile.LicensePlate;
            var vehicle = await db.Vehicles.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.VehicleId == vehicleId ||
                item.VehicleCode == vehicleCode || (licensePlate != null && item.LicensePlate == licensePlate), cancellationToken);
            if (vehicle is null)
            {
                vehicle = new Vehicle
                {
                    VehicleId = vehicleId,
                    VehicleCode = vehicleCode,
                    Status = VehicleStatus.AVAILABLE,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                ApplyVehicleDetails(vehicle, vehicleProfile, expiryDate);
                db.Vehicles.Add(vehicle);
                vehicles++;
            }
            else if (vehicle.VehicleId == vehicleId && !vehicle.IsDeleted &&
                (vehicle.VehicleName?.StartsWith(LegacyDemoVehiclePrefix, StringComparison.Ordinal) == true ||
                 vehicle.VehicleName == $"Pegaxus {vehicleProfile.Mode} {number:000}"))
            {
                ApplyVehicleDetails(vehicle, vehicleProfile, expiryDate);
                vehicle.UpdatedAt = now;
            }

            var providerProfile = ProviderProfiles[number - 1];
            var providerCode = $"PRV-20261010-{number:0000}";
            var providerId = StableGuid(providerCode);
            var taxCode = providerProfile.TaxCode;
            var provider = await db.TransportProviders.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.TransportProviderId == providerId ||
                item.ProviderCode == providerCode || item.TaxCode == taxCode, cancellationToken);
            if (provider is null)
            {
                provider = new TransportProvider
                {
                    TransportProviderId = providerId,
                    ProviderCode = providerCode,
                    Status = ProviderStatus.ACTIVE,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                ApplyProviderDetails(provider, providerProfile);
                db.TransportProviders.Add(provider);
                providers++;
            }
            else if (provider.TransportProviderId == providerId && !provider.IsDeleted &&
                (provider.ProviderName?.StartsWith(LegacyDemoProviderPrefix, StringComparison.Ordinal) == true ||
                 provider.ProviderName == $"Pegaxus Transport {number:000}"))
            {
                ApplyProviderDetails(provider, providerProfile);
                provider.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return (vehicles, providers);
    }

    private static void ApplyVehicleDetails(Vehicle vehicle, VehicleProfile profile, DateOnly expiryDate)
    {
        vehicle.VehicleName = profile.Name;
        vehicle.TransportMode = profile.Mode;
        vehicle.LicensePlate = profile.LicensePlate;
        vehicle.VehicleType = profile.Type;
        vehicle.CapacityHorses = profile.Capacity;
        vehicle.RegistrationNumber = profile.RegistrationNumber;
        vehicle.RegistrationExpiryDate = expiryDate;
        vehicle.InsuranceNumber = profile.InsuranceNumber;
        vehicle.InsuranceExpiryDate = expiryDate;
        vehicle.SpecialEquipment = profile.SpecialEquipment;
        vehicle.Country = profile.Country;
    }

    private static void ApplyProviderDetails(TransportProvider provider, ProviderProfile profile)
    {
        provider.ProviderName = profile.Name;
        provider.ContactName = profile.ContactName;
        provider.Email = profile.Email;
        provider.Country = profile.Country;
        provider.TaxCode = profile.TaxCode;
        provider.SupportedModes = profile.SupportedModes;
        provider.Note = profile.Note;
    }

    public static async Task<int> SeedLocationsAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        using var stream = typeof(AppDbContextSeedData).Assembly
            .GetManifestResourceStream("Pegaxus.API.Data.locations-camelcase.json")
            ?? throw new InvalidDataException("Location seed resource is missing.");
        var records = JsonNode.Parse(stream)?.AsArray()
            ?? throw new InvalidDataException("Location seed must be an array.");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        var sourceIds = new HashSet<int>();
        var names = new HashSet<(string, string)>();
        var locations = new List<Location>();
        foreach (var record in records)
        {
            if (record is not JsonObject row || row["locationId"]?.GetValue<int>() is not int id ||
                id <= 0 || !sourceIds.Add(id))
                throw new InvalidDataException("Location seed IDs must be positive and unique.");
            row["locationId"] = JsonValue.Create(StableGuid($"LOC-CATALOG:{id}"));
            var location = row.Deserialize<Location>(options)
                ?? throw new InvalidDataException($"Invalid location seed: {id}.");
            if (string.IsNullOrWhiteSpace(location.LocationName) || location.LocationName.Length > 200 ||
                string.IsNullOrWhiteSpace(location.Country) || location.Country.Length > 100 ||
                location.Address?.Length > 500 || location.City?.Length > 100 || location.StateProvince?.Length > 100 ||
                location.LocationType is null || location.Latitude is < -90 or > 90 || location.Longitude is < -180 or > 180 ||
                location.CreatedAt == default || location.UpdatedAt < location.CreatedAt ||
                !names.Add((location.LocationName.Trim().ToUpperInvariant(), location.Country.Trim().ToUpperInvariant())))
                throw new InvalidDataException($"Invalid or duplicate location seed: {id}.");
            location.CreatedAt = DateTime.SpecifyKind(location.CreatedAt.ToUniversalTime(), DateTimeKind.Unspecified);
            location.UpdatedAt = DateTime.SpecifyKind(location.UpdatedAt.ToUniversalTime(), DateTimeKind.Unspecified);
            if (location.DeletedAt is { } deletedAt)
                location.DeletedAt = DateTime.SpecifyKind(deletedAt.ToUniversalTime(), DateTimeKind.Unspecified);
            locations.Add(location);
        }

        var existing = await db.Locations.IgnoreQueryFilters()
            .Select(item => new { item.LocationId, item.LocationName, item.Country })
            .ToArrayAsync(cancellationToken);
        var existingIds = existing.Select(item => item.LocationId).ToHashSet();
        var existingNames = existing.Select(item =>
            (item.LocationName.Trim().ToUpperInvariant(), item.Country.Trim().ToUpperInvariant())).ToHashSet();
        var additions = locations.Where(item => !existingIds.Contains(item.LocationId) &&
            !existingNames.Contains((item.LocationName.Trim().ToUpperInvariant(), item.Country.Trim().ToUpperInvariant())))
            .ToArray();
        db.Locations.AddRange(additions);
        await db.SaveChangesAsync(cancellationToken);
        return additions.Length;
    }

    private static async Task SeedDemoOperationsAsync(
        AppDbContext db,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var customers = await db.Customers
            .Include(item => item.Account)
            .Where(item => item.Account.Email == "trinhnganquan@gmail.com" ||
                          item.Account.Email == "batquyquan@gmail.com" ||
                          item.Account.Email == "kimvetuan@gmail.com" ||
                          item.Account.Email == "khuyenvkse200481@fpt.edu.vn")
            .ToDictionaryAsync(item => item.Account.Email, cancellationToken);
        var employees = await db.Employees
            .Include(item => item.Account)
            .Where(item => item.Account.Email == "diechahien@gmail.com" ||
                          item.Account.Email == "huyvog6226@gmail.com" ||
                          item.Account.Email == "hoanguyen91011@gmail.com" ||
                          item.Account.Email == "duongngo2209@gmail.com" ||
                          item.Account.Email == "quocbao.nguyen16102006@gmail.com")
            .ToDictionaryAsync(item => item.Account.Email, cancellationToken);

        var horses = await SeedDemoHorsesAsync(db, customers, now, cancellationToken);
        await SeedDemoBookingsAsync(db, customers, employees, horses, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var bookings = await db.Bookings
            .Where(item => item.BookingCode.StartsWith("REQ-20261010-"))
            .ToDictionaryAsync(item => item.BookingCode, cancellationToken);
        var transports = await SeedDemoTransportsAsync(db, bookings, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var templates = await SeedDemoTemplatesAsync(db, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await SeedDemoDossiersAsync(db, transports, templates, horses, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    // Horses still carrying a placeholder passport (not 15 characters, so the horse could not even be edited) are upgraded once.
    private const string LegacyDemoPassportPrefix = "PEGAXUS-DEMO-";

    private static async Task<Dictionary<string, Horse>> SeedDemoHorsesAsync(
        AppDbContext db,
        IReadOnlyDictionary<string, Customer> customers,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, Horse>(StringComparer.OrdinalIgnoreCase);
        var orderedCustomerEmails = new[]
        {
            "trinhnganquan@gmail.com",
            "batquyquan@gmail.com",
            "kimvetuan@gmail.com",
            "khuyenvkse200481@fpt.edu.vn"
        };
        var seeds = orderedCustomerEmails.SelectMany((customerEmail, customerIndex) =>
                HorseProfiles[customerIndex].Select((profile, horseIndex) => new HorseSeed(
                    customerEmail,
                    $"HRS-20261010-{customerIndex * 4 + horseIndex + 1:0000}",
                    profile,
                    $"985112{customerIndex + 1:000}{horseIndex + 1:000000}",
                    $"250PEG{customerIndex + 1:000}{horseIndex + 1:000000}")))
            .ToArray();

        foreach (var seed in seeds)
        {
            var customerId = customers[seed.CustomerEmail].CustomerId;
            // The seed codes were renamed once, which changed the IDs derived from them. A horse an earlier seed made keeps its old ID
            // but still carries this seed's microchip, so it is reused instead of inserted again.
            var horse = await db.Horses.IgnoreQueryFilters()
                .Where(item => item.HorseId == seed.Id || !item.IsDeleted && item.CustomerId == customerId &&
                    (item.MicrochipNumber == seed.MicrochipNumber || item.PassportNumber == seed.PassportNumber))
                .OrderByDescending(item => item.HorseId == seed.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (horse is null)
            {
                horse = new Horse
                {
                    HorseId = seed.Id,
                    CustomerId = customerId,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                ApplyHorseDetails(horse, seed);
                db.Horses.Add(horse);
            }
            else if (!horse.IsDeleted &&
                (horse.PassportNumber?.StartsWith(LegacyDemoPassportPrefix, StringComparison.Ordinal) == true ||
                 horse.PassportNumber == $"PEGAXUS-{seed.Code}"))
            {
                ApplyHorseDetails(horse, seed);
                horse.UpdatedAt = now;
            }
            result[seed.Code] = horse;
        }

        return result;
    }

    private static void ApplyHorseDetails(Horse horse, HorseSeed seed)
    {
        var profile = seed.Profile;
        horse.HorseName = profile.Name;
        horse.Sex = profile.Sex;
        horse.Breed = profile.Breed;
        horse.Color = profile.Color;
        horse.DateOfBirth = profile.DateOfBirth;
        horse.CountryOfOrigin = profile.Country;
        horse.WeightKg = profile.WeightKg;
        horse.HeightCm = profile.HeightCm;
        horse.MicrochipNumber = seed.MicrochipNumber;
        horse.PassportNumber = seed.PassportNumber;
        horse.IdentificationNotes = profile.Notes;
    }

    // Bookings still carrying this note were never touched, so they are brought up to date once.
    private const string LegacyDemoBookingNote = "Seeded demo booking for Swagger testing.";

    private static async Task SeedDemoBookingsAsync(
        AppDbContext db,
        IReadOnlyDictionary<string, Customer> customers,
        IReadOnlyDictionary<string, Employee> employees,
        IReadOnlyDictionary<string, Horse> horses,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Departures follow the booking rule (domestic 21 days, international 2 months ahead) from the day the seed runs.
        var today = DateOnly.FromDateTime(now);
        var seeds = new[]
        {
            // A same-country route is only accepted inside the United Kingdom, so the draft demo booking crosses to England.
            new BookingSeed("REQ-20261010-0001", "trinhnganquan@gmail.com", BookingStatus.DRAFT, Chantilly, Newbury, 75, 1,
                "Hay net and water stop every four hours.", "Both horses are moving to the summer meeting at Newbury.", null,
                new[] { "HRS-20261010-0001", "HRS-20261010-0002" }),
            new BookingSeed("REQ-20261010-0002", "batquyquan@gmail.com", BookingStatus.PENDING, Grosbois, NationalStud, 90, 4,
                "Temperature monitoring required throughout the journey.", "Two horses are going to a training yard for the autumn season.", null,
                new[] { "HRS-20261010-0005", "HRS-20261010-0006" }),
            new BookingSeed("REQ-20261010-0003", "kimvetuan@gmail.com", BookingStatus.APPROVED, Longchamp, DalhamHall, 100, 5,
                "Border control post inspection required on arrival in the United Kingdom.", "Three horses are travelling to the Newmarket sales.", null,
                new[] { "HRS-20261010-0009", "HRS-20261010-0010", "HRS-20261010-0011" }),
            new BookingSeed("REQ-20261010-0004", "khuyenvkse200481@fpt.edu.vn", BookingStatus.APPROVED, Curragh, Chantilly, 110, 4,
                "Overnight stabling needed before the sea crossing.", "Two horses are joining a stable in Chantilly.", null,
                new[] { "HRS-20261010-0013", "HRS-20261010-0014" }),
            new BookingSeed("REQ-20261010-0005", "trinhnganquan@gmail.com", BookingStatus.APPROVED, Chantilly, Ascot, 120, 5,
                "Collection at 06:00; vet check before loading.", "Entries confirmed for the Ascot meeting.", null,
                new[] { "HRS-20261010-0003", "HRS-20261010-0004" }),
            new BookingSeed("REQ-20261010-0006", "batquyquan@gmail.com", BookingStatus.REJECTED, Newbury, Quesnay, 130, 5,
                "Return after the autumn sales.", "One horse is coming back to its home stud.", "The passport of the horse expires before the departure date.",
                new[] { "HRS-20261010-0007" })
        };

        foreach (var seed in seeds)
        {
            var existing = await db.Bookings.SingleOrDefaultAsync(item => item.BookingCode == seed.BookingCode, cancellationToken);
            if (existing is not null)
            {
                // Seed bookings written before country names were used hold ISO codes (and the draft one a domestic French
                // route the booking rules reject); bring an editable one back in line so it can be saved from the UI.
                if (existing.Status is BookingStatus.DRAFT or BookingStatus.PENDING &&
                    (CountryUtils.CountryName(existing.OriginCountry) != existing.OriginCountry ||
                     CountryUtils.CountryName(existing.DestinationCountry) != existing.DestinationCountry ||
                     !CountryUtils.ValidateRoute(existing.OriginCountry, existing.DestinationCountry).IsValid))
                {
                    ApplyBookingDetails(existing, seed, today);
                    existing.UpdatedAt = now;
                    continue;
                }
                if (existing.Status == seed.Status &&
                    (existing.CustomerNote == LegacyDemoBookingNote || existing.CustomerNote == "Please confirm transport arrangements before departure."))
                {
                    ApplyBookingDetails(existing, seed, today);
                    existing.UpdatedAt = now;
                }
                continue;
            }

            var customer = customers[seed.CustomerEmail];
            var booking = new Booking
            {
                BookingId = seed.Id,
                BookingCode = seed.BookingCode,
                CustomerId = customer.CustomerId,
                AssignedEmployeeId = employees["diechahien@gmail.com"].EmployeeId,
                Status = seed.Status,
                SubmittedAt = seed.Status == BookingStatus.DRAFT ? null : now.AddDays(-5),
                ApprovedAt = seed.Status == BookingStatus.APPROVED ? now.AddDays(-3) : null,
                RejectedAt = seed.Status == BookingStatus.REJECTED ? now.AddDays(-3) : null,
                CreatedAt = now,
                UpdatedAt = now
            };
            ApplyBookingDetails(booking, seed, today);
            db.Bookings.Add(booking);
            foreach (var horseCode in seed.HorseCodes)
                db.BookingHorses.Add(new BookingHorse
                {
                    BookingId = booking.BookingId,
                    HorseId = horses[horseCode].HorseId,
                    SpecialHandlingNotes = "Handle according to horse profile.",
                    CreatedAt = now
                });

            var changedBy = seed.Status == BookingStatus.DRAFT ? customer.AccountId : employees["diechahien@gmail.com"].AccountId;
            if (seed.Status != BookingStatus.DRAFT)
                db.BookingStatusHistories.Add(new BookingStatusHistory
                {
                    BookingStatusHistoryId = StableGuid($"{seed.BookingCode}:submit"),
                    BookingId = booking.BookingId,
                    Type = BookingHistoryType.SUBMIT,
                    PreviousStatus = BookingStatus.DRAFT,
                    NewStatus = BookingStatus.PENDING,
                    ChangedByAccountId = customer.AccountId,
                    ChangedAt = now.AddDays(-5)
                });
            if (seed.Status is BookingStatus.APPROVED or BookingStatus.REJECTED)
                db.BookingStatusHistories.Add(new BookingStatusHistory
                {
                    BookingStatusHistoryId = StableGuid($"{seed.BookingCode}:final"),
                    BookingId = booking.BookingId,
                    Type = seed.Status == BookingStatus.APPROVED ? BookingHistoryType.APPROVE : BookingHistoryType.REJECT,
                    PreviousStatus = BookingStatus.PENDING,
                    NewStatus = seed.Status,
                    ChangedByAccountId = changedBy,
                    Reason = seed.RejectionReason,
                    ChangedAt = now.AddDays(-3)
                });
        }
    }

    private static void ApplyBookingDetails(Booking booking, BookingSeed seed, DateOnly today)
    {
        booking.OriginAddress = seed.Origin.Address;
        booking.OriginCountry = CountryUtils.CountryName(seed.Origin.Country) ?? seed.Origin.Country;
        booking.OriginLatitude = seed.Origin.Latitude;
        booking.OriginLongitude = seed.Origin.Longitude;
        booking.DestinationAddress = seed.Destination.Address;
        booking.DestinationCountry = CountryUtils.CountryName(seed.Destination.Country) ?? seed.Destination.Country;
        booking.DestinationLatitude = seed.Destination.Latitude;
        booking.DestinationLongitude = seed.Destination.Longitude;
        booking.RequestedDepartureDate = today.AddDays(seed.DepartureInDays);
        booking.RequestedArrivalDate = today.AddDays(seed.DepartureInDays + seed.TripDays);
        booking.SpecialRequirements = seed.SpecialRequirements;
        booking.CustomerNote = seed.CustomerNote;
        booking.RejectionReason = seed.RejectionReason;
    }

    private static async Task<Dictionary<string, Transport>> SeedDemoTransportsAsync(
        AppDbContext db,
        IReadOnlyDictionary<string, Booking> bookings,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var seeds = new[]
        {
            new TransportSeed("TR-20261010-0003", "REQ-20261010-0003", TransportStatus.CREATED),
            new TransportSeed("TR-20261010-0004", "REQ-20261010-0004", TransportStatus.PLANNING),
            new TransportSeed("TR-20261010-0005", "REQ-20261010-0005", TransportStatus.READY)
        };
        var result = new Dictionary<string, Transport>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in seeds)
        {
            var transport = await db.Transports.SingleOrDefaultAsync(item => item.TransportCode == seed.TransportCode, cancellationToken);
            if (transport is null)
            {
                transport = new Transport
                {
                    TransportId = seed.Id,
                    TransportCode = seed.TransportCode,
                    BookingId = bookings[seed.BookingCode].BookingId,
                    Status = seed.Status,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Transports.Add(transport);
            }
            result[seed.BookingCode] = transport;
        }
        return result;
    }

    private static async Task<Dictionary<string, DossierTemplate>> SeedDemoTemplatesAsync(AppDbContext db, DateTime now, CancellationToken cancellationToken)
    {
        var requirementIds = await db.ComplianceRequirements.ToDictionaryAsync(item => item.RequirementCode, item => item.ComplianceRequirementId, cancellationToken);
        var seeds = new[]
        {
            new TemplateSeed("TPL-EU-GB", "EU to GB Equine Transport", DossierTemplateStatus.ACTIVE, new[] { "EQUINE_PASSPORT", "EHC_EU_TO_GB", "IPAFFS_PRE_NOTIF", "ATA_CARNET_EXPORT" }),
            new TemplateSeed("TPL-EQUINE-IDENTITY", "Equine Identity and Journey Log", DossierTemplateStatus.DRAFT, new[] { "EQUINE_PASSPORT", "JOURNEY_LOG_EC1_2005" })
        };
        var result = new Dictionary<string, DossierTemplate>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in seeds)
        {
            var template = await db.DossierTemplates.SingleOrDefaultAsync(item => item.TemplateCode == seed.Code, cancellationToken);
            if (template is null)
            {
                template = new DossierTemplate
                {
                    DossierTemplateId = seed.Id,
                    TemplateCode = seed.Code,
                    TemplateName = seed.Name,
                    Status = seed.Status,
                    Description = "Document requirements for equine transport.",
                    VersionNo = 1,
                    EffectiveFrom = new DateOnly(2026, 1, 1),
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.DossierTemplates.Add(template);
                for (var index = 0; index < seed.RequirementCodes.Length; index++)
                    db.DossierTemplateItems.Add(new DossierTemplateItem
                    {
                        DossierTemplateItemId = StableGuid($"{seed.Code}:{seed.RequirementCodes[index]}"),
                        DossierTemplateId = template.DossierTemplateId,
                        ComplianceRequirementId = requirementIds[seed.RequirementCodes[index]],
                        SequenceNo = index + 1,
                        IsMandatory = true,
                        AppliesPerHorse = seed.RequirementCodes[index] == "EQUINE_PASSPORT",
                        Instruction = "Provide a valid document for the transport.",
                        CreatedAt = now,
                        UpdatedAt = now
                    });
            }
            result[seed.Code] = template;
        }
        return result;
    }

    private static async Task SeedDemoDossiersAsync(AppDbContext db, IReadOnlyDictionary<string, Transport> transports, IReadOnlyDictionary<string, DossierTemplate> templates, IReadOnlyDictionary<string, Horse> horses, DateTime now, CancellationToken cancellationToken)
    {
        var transport = transports["REQ-20261010-0005"];
        if (await db.ComplianceDossiers.AnyAsync(item => item.DossierCode == "CD-20261010-0001", cancellationToken))
            return;

        var requirements = await db.ComplianceRequirements
            .Where(item => item.RequirementCode == "EQUINE_PASSPORT" || item.RequirementCode == "EHC_EU_TO_GB" || item.RequirementCode == "IPAFFS_PRE_NOTIF")
            .ToDictionaryAsync(item => item.RequirementCode, cancellationToken);
        var dossier = new ComplianceDossier
        {
            ComplianceDossierId = StableGuid("CD-20261010-0001"),
            DossierCode = "CD-20261010-0001",
            TransportId = transport.TransportId,
            DossierTemplateId = templates["TPL-EU-GB"].DossierTemplateId,
            Status = ComplianceDossierStatus.IN_PREPARATION,
            OpenedAt = now.AddDays(-1),
            Note = "Prepare passport, health certificate and import pre-notification.",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ComplianceDossiers.Add(dossier);
        db.DossierRequirements.AddRange(
            new DossierRequirement
            {
                DossierRequirementId = StableGuid("CD-20261010-0001:EQUINE_PASSPORT"),
                ComplianceDossierId = dossier.ComplianceDossierId,
                ComplianceRequirementId = requirements["EQUINE_PASSPORT"].ComplianceRequirementId,
                HorseId = horses["HRS-20261010-0003"].HorseId,
                Status = DossierRequirementStatus.PROVIDED,
                IsMandatory = true,
                DueDate = DateOnly.FromDateTime(now).AddDays(70),
                CreatedAt = now,
                UpdatedAt = now
            },
            new DossierRequirement
            {
                DossierRequirementId = StableGuid("CD-20261010-0001:EHC_EU_TO_GB"),
                ComplianceDossierId = dossier.ComplianceDossierId,
                ComplianceRequirementId = requirements["EHC_EU_TO_GB"].ComplianceRequirementId,
                Status = DossierRequirementStatus.PENDING,
                IsMandatory = true,
                DueDate = DateOnly.FromDateTime(now).AddDays(75),
                CreatedAt = now,
                UpdatedAt = now
            },
            new DossierRequirement
            {
                DossierRequirementId = StableGuid("CD-20261010-0001:IPAFFS_PRE_NOTIF"),
                ComplianceDossierId = dossier.ComplianceDossierId,
                ComplianceRequirementId = requirements["IPAFFS_PRE_NOTIF"].ComplianceRequirementId,
                Status = DossierRequirementStatus.REQUESTED,
                IsMandatory = true,
                DueDate = DateOnly.FromDateTime(now).AddDays(80),
                CreatedAt = now,
                UpdatedAt = now
            });
    }

    private static Guid StableGuid(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        var guidBytes = bytes[..16];
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes, bigEndian: true);
    }

    private static async Task SeedEmployeeAsync(
        AppDbContext db,
        IPasswordHasher<Account> passwordHasher,
        EmployeeSeed seed,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var email = seed.Email.ToLowerInvariant();
        var account = await db.Accounts
            .Include(item => item.EmployeeAccount)
            .SingleOrDefaultAsync(item => item.Email.ToLower() == email, cancellationToken);

        if (account is not null)
        {
            if (account.EmployeeAccount is null)
                throw new InvalidOperationException($"Seed email already belongs to a different account type: {seed.Email}");
            account.EmployeeAccount.EmployeeCode = seed.EmployeeCode;
            return;
        }

        account = new Account
        {
            AccountId = Guid.NewGuid(),
            Email = email,
            AccountType = AccountType.EMPLOYEE,
            Status = AccountStatus.ACTIVE,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        account.PasswordHash = passwordHasher.HashPassword(account, DefaultPassword);

        db.Accounts.Add(account);
        db.Employees.Add(new Employee
        {
            EmployeeId = Guid.NewGuid(),
            AccountId = account.AccountId,
            EmployeeCode = seed.EmployeeCode,
            Role = seed.Role,
            FullName = seed.FullName,
            Phone = seed.Phone,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private static async Task SeedCustomerAsync(
        AppDbContext db,
        IPasswordHasher<Account> passwordHasher,
        CustomerSeed seed,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var email = seed.Email.ToLowerInvariant();
        var account = await db.Accounts
            .Include(item => item.CustomerAccount)
            .SingleOrDefaultAsync(item => item.Email.ToLower() == email, cancellationToken);

        if (account is not null)
        {
            if (account.CustomerAccount is null)
                throw new InvalidOperationException($"Seed email already belongs to a different account type: {seed.Email}");
            return;
        }

        account = new Account
        {
            AccountId = Guid.NewGuid(),
            Email = email,
            AccountType = AccountType.CUSTOMER,
            Status = AccountStatus.ACTIVE,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        account.PasswordHash = passwordHasher.HashPassword(account, DefaultPassword);

        db.Accounts.Add(account);
        db.Customers.Add(new Customer
        {
            CustomerId = Guid.NewGuid(),
            AccountId = account.AccountId,
            CustomerCode = await GenerateCustomerCodeAsync(db, cancellationToken),
            FullName = seed.FullName,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private static Task<string> GenerateCustomerCodeAsync(AppDbContext db, CancellationToken cancellationToken) =>
        CodeGeneration.NextAsync("CUS", DateTime.UtcNow, async code =>
            db.Customers.Local.Any(item => item.CustomerCode == code) ||
            await db.Customers.AnyAsync(item => item.CustomerCode == code, cancellationToken));

    private static async Task SeedComplianceRequirementsAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var seeds = ComplianceRequirementSeeds;
        var existingCodes = await db.ComplianceRequirements
            .Where(item => seeds.Select(seed => seed.RequirementCode).Contains(item.RequirementCode))
            .Select(item => item.RequirementCode)
            .ToListAsync(cancellationToken);
        var existingCodeSet = existingCodes.ToHashSet(StringComparer.Ordinal);

        foreach (var seed in seeds)
        {
            if (existingCodeSet.Contains(seed.RequirementCode))
                continue;

            db.ComplianceRequirements.Add(new ComplianceRequirement
            {
                ComplianceRequirementId = StableGuid($"REQUIREMENT:{seed.RequirementCode}"),
                RequirementCode = seed.RequirementCode,
                RequirementName = seed.RequirementName,
                Description = seed.Description,
                Country = seed.Country,
                Category = seed.Category,
                IssuingAuthority = seed.IssuingAuthority,
                BcpCode = seed.BcpCode,
                LegalBasis = seed.LegalBasis,
                ValidityRequired = seed.ValidityRequired,
                ValidityPeriodDays = seed.ValidityPeriodDays,
                Status = seed.Status,
                IsDeleted = false,
                DeletedAt = null,
                DeletedByAccountId = null,
                CreatedAt = ComplianceRequirementSeedTimestamp,
                UpdatedAt = ComplianceRequirementSeedTimestamp
            });
        }
    }

    private static readonly ComplianceRequirementSeed[] ComplianceRequirementSeeds =
    [
        new("EQUINE_PASSPORT", "Equine Passport", "Used to verify the horse's identity, passport number, and microchip.", "EU", ComplianceRequirementCategory.HorseIdentity, "Weatherbys / France Galop / Approved Passport Issuing Organisation", null, "Equine identification and passport control requirements.", true, 365, ComplianceRequirementStatus.ACTIVE),
        new("EHC_GB_TO_EU_8438", "Export Health Certificate GB to EU — Form 8438", "Veterinary export health certificate for horses travelling from Great Britain to the European Union.", "GB", ComplianceRequirementCategory.HorseHealth, "APHA / Official Veterinarian", null, "Export health certification for equidae transported from GB to EU.", true, 10, ComplianceRequirementStatus.ACTIVE),
        new("EHC_EU_TO_GB", "Export Health Certificate EU to GB", "Veterinary export health certificate for horses travelling from the European Union to Great Britain.", "EU", ComplianceRequirementCategory.HorseHealth, "EU Official Veterinarian / TRACES-NT", null, "Export health certification for equidae transported from EU to GB.", true, 10, ComplianceRequirementStatus.ACTIVE),
        new("LAB_TEST_EIA_COGGINS", "Laboratory Test Report — EIA Coggins Test", "Laboratory result confirming the horse's Coggins or EIA test result when required for the route.", null, ComplianceRequirementCategory.HorseHealth, "Approved Veterinary Laboratory", null, "Equine infectious anaemia testing requirement for applicable routes.", true, 90, ComplianceRequirementStatus.ACTIVE),
        new("CHED_A_DOCUMENT", "CHED-A Animal Entry Document", "Animal entry health declaration required for controlled routes through an EU border control post.", "EU", ComplianceRequirementCategory.Quarantine, "TRACES-NT / EU Border Control Post", "FRCAL1", "Common Health Entry Document for Animals.", true, 1, ComplianceRequirementStatus.ACTIVE),
        new("IPAFFS_PRE_NOTIF", "IPAFFS Import Pre-notification", "Advance notification required before horses enter Great Britain through IPAFFS.", "GB", ComplianceRequirementCategory.Quarantine, "UK Defra IPAFFS", "GBHOL1", "Pre-notification requirement for applicable imports into GB.", true, 1, ComplianceRequirementStatus.ACTIVE),
        new("ATA_CARNET_EXPORT", "ATA Carnet — Export Voucher", "Temporary export document for horses and related equipment.", "GB", ComplianceRequirementCategory.Customs, "Chamber of Commerce / Customs Authority", null, "Temporary admission and re-export customs procedure.", true, 365, ComplianceRequirementStatus.ACTIVE),
        new("ATA_CARNET_IMPORT", "ATA Carnet — Import Voucher", "Temporary import document for the destination country.", "EU", ComplianceRequirementCategory.Customs, "Chamber of Commerce / Customs Authority", null, "Temporary admission customs procedure at destination country.", true, 365, ComplianceRequirementStatus.ACTIVE),
        new("JOURNEY_LOG_EC1_2005", "EU Animal Transport Journey Log", "Records the travel plan, driving time, rest time, and any exceptions during the journey.", "EU", ComplianceRequirementCategory.Transport, "Transport Operator / Competent Authority", null, "Animal welfare and journey log requirements for transported equidae.", false, null, ComplianceRequirementStatus.ACTIVE),
        new("DRIVER_COMPETENCE_CERT", "Driver Competence Certificate", "Certificate confirming a driver's competence to transport live animals.", null, ComplianceRequirementCategory.Transport, "Competent Transport Authority / Approved Training Body", null, "Driver competence requirement for transport of live animals.", true, 365, ComplianceRequirementStatus.INACTIVE)
    ];

    private sealed record EmployeeSeed(
        string FullName,
        string EmployeeCode,
        string Email,
        string Phone,
        EmployeeRole Role);

    private sealed record CustomerSeed(string FullName, string Email);

    private sealed record HorseProfile(
        string Name,
        HorseSex Sex,
        string Breed,
        string Color,
        DateOnly DateOfBirth,
        decimal WeightKg,
        decimal HeightCm,
        string Country,
        string Notes);

    private sealed record HorseSeed(
        string CustomerEmail,
        string Code,
        HorseProfile Profile,
        string MicrochipNumber,
        string PassportNumber)
    {
        public Guid Id => StableGuid(Code);
    }

    private sealed record Place(string Address, string Country, decimal Latitude, decimal Longitude);

    private sealed record BookingSeed(
        string BookingCode,
        string CustomerEmail,
        BookingStatus Status,
        Place Origin,
        Place Destination,
        int DepartureInDays,
        int TripDays,
        string SpecialRequirements,
        string CustomerNote,
        string? RejectionReason,
        string[] HorseCodes)
    {
        public Guid Id => StableGuid(BookingCode);
    }

    private sealed record VehicleProfile(
        string Name,
        TransportMode Mode,
        string? LicensePlate,
        string Type,
        int Capacity,
        string RegistrationNumber,
        string InsuranceNumber,
        string SpecialEquipment,
        string Country);

    private sealed record ProviderProfile(
        string Name,
        string ContactName,
        string Email,
        string Country,
        string TaxCode,
        string SupportedModes,
        string Note);

    // Real venues from the location catalogue, so the demo bookings point at places that exist on the map.
    private static readonly Place Chantilly = new("Centre d'Entraînement de Chantilly (France Galop), Avenue de la Plaine des Aigles, 60500 Chantilly", "FR", 49.1931m, 2.4722m);
    private static readonly Place Grosbois = new("Centre d'Entraînement de Grosbois, Domaine de Grosbois, 94440 Marolles-en-Brie", "FR", 48.7369m, 2.5422m);
    private static readonly Place Longchamp = new("Hippodrome de ParisLongchamp Écuries, 2 Route des Tribunes, 75016 Paris", "FR", 48.8578m, 2.2345m);
    private static readonly Place Quesnay = new("Haras du Quesnay, Route du Quesnay, 14800 Vauville", "FR", 49.3145m, 0.0512m);
    private static readonly Place NationalStud = new("The National Stud Newmarket, July Racecourse, Newmarket, Suffolk CB8 0XE", "GB", 52.2281m, 0.3845m);
    private static readonly Place DalhamHall = new("Godolphin Dalham Hall Stud, Duchess Drive, Newmarket, Suffolk CB8 9HE", "GB", 52.2198m, 0.4485m);
    private static readonly Place Ascot = new("Ascot Racecourse Stables Complex, High Street, Ascot, Berkshire SL5 7JX", "GB", 51.4162m, -0.6763m);
    private static readonly Place Newbury = new("Newbury Racecourse Equine Facility, Racecourse Road, Newbury, Berkshire RG14 7NZ", "GB", 51.3934m, -1.3061m);
    private static readonly Place Curragh = new("The Curragh Racecourse & Training Grounds, The Curragh, Co. Kildare, R56 VX61", "IE", 53.1589m, -6.8289m);

    // Four horses per demo customer, in the order of the customer list in SeedDemoHorsesAsync.
    private static readonly HorseProfile[][] HorseProfiles =
    [
        [
            new("Eclipse Valor", HorseSex.MALE, "Thoroughbred", "Bay", new DateOnly(2019, 4, 12), 512, 165.5m, "France", "White star and snip; white sock on the near hind."),
            new("Desert Mirage", HorseSex.FEMALE, "Arabian", "Grey", new DateOnly(2020, 3, 8), 432, 150, "France", "Dished profile; dark skin around the eyes."),
            new("Northern Lights", HorseSex.GELDING, "Selle Français", "Chestnut", new DateOnly(2018, 5, 21), 565, 168, "France", "Broad blaze; two white socks behind."),
            new("Golden Sovereign", HorseSex.MALE, "Anglo-Arabian", "Bay", new DateOnly(2021, 2, 17), 478, 160, "France", "Small white star; calm in the box but anxious when loading.")
        ],
        [
            new("Storm Chaser", HorseSex.MALE, "Irish Sport Horse", "Dark Bay", new DateOnly(2017, 6, 2), 592, 170, "Ireland", "No white markings; scar on the near shoulder."),
            new("Velvet Whisper", HorseSex.FEMALE, "Hanoverian", "Black", new DateOnly(2019, 9, 14), 541, 166, "Germany", "Small white star; brand on the left hindquarter."),
            new("Copper Ridge", HorseSex.GELDING, "Irish Draught", "Chestnut", new DateOnly(2016, 4, 30), 618, 168, "Ireland", "Wide blaze; white off-fore sock."),
            new("Starlit Meadow", HorseSex.FEMALE, "Connemara", "Grey", new DateOnly(2020, 7, 19), 392, 142, "Ireland", "Dappled coat; dark mane and tail.")
        ],
        [
            new("Argent Fortune", HorseSex.MALE, "Thoroughbred", "Bay", new DateOnly(2018, 3, 25), 524, 163, "United Kingdom", "Narrow stripe; white coronet on the near fore."),
            new("Lady Marmalade", HorseSex.FEMALE, "Cleveland Bay", "Bay", new DateOnly(2019, 5, 11), 558, 162, "United Kingdom", "Black points; no white markings."),
            new("Highland Ember", HorseSex.GELDING, "Welsh Cob", "Chestnut", new DateOnly(2017, 10, 3), 482, 150, "United Kingdom", "Flaxen mane; white snip."),
            new("Silverbrook Jet", HorseSex.MALE, "Dutch Warmblood", "Dark Bay", new DateOnly(2020, 1, 28), 571, 172, "Netherlands", "Small star; whorl on the forehead.")
        ],
        [
            new("Étoile du Matin", HorseSex.FEMALE, "Selle Français", "Bay", new DateOnly(2019, 8, 9), 531, 165, "France", "Faint star; white off-hind pastern."),
            new("Baron von Rhein", HorseSex.MALE, "Oldenburg", "Black", new DateOnly(2018, 11, 15), 602, 174, "Germany", "Large star; white off-hind pastern."),
            new("Midnight Frisian", HorseSex.GELDING, "Friesian", "Black", new DateOnly(2016, 5, 23), 641, 165, "Netherlands", "Feathering on all four legs."),
            new("Honey Bay Dancer", HorseSex.FEMALE, "Lusitano", "Bay", new DateOnly(2021, 6, 1), 468, 157, "Portugal", "Faint star; one white sock off-fore.")
        ]
    ];

    // Ten vehicles in code order: six road, two air, two sea.
    private static readonly VehicleProfile[] VehicleProfiles =
    [
        new("Mercedes-Benz Actros Horse Lorry", TransportMode.ROAD, "GH-482-PL", "Horse truck", 6, "FR-REG-2021-48213", "INS-FR-775201", "Air suspension; automatic watering; CCTV; temperature monitoring", "FR"),
        new("Volvo FH Equine Transporter", TransportMode.ROAD, "HK-207-TR", "Horse truck", 8, "FR-REG-2022-10457", "INS-FR-775344", "Air suspension; hydraulic ramp; CCTV; ventilation control", "FR"),
        new("DAF XF Horse Lorry", TransportMode.ROAD, "EJ21 FXT", "Horse truck", 4, "GB-REG-2021-33910", "INS-GB-208117", "Ventilation fans; rubber flooring; temperature monitoring", "GB"),
        new("Iveco Eurocargo Horse Box", TransportMode.ROAD, "KL19 RBN", "Horse truck", 3, "GB-REG-2019-27764", "INS-GB-208452", "Padded partitions; rubber flooring; ventilation", "GB"),
        new("Scania R450 Horse Transporter", TransportMode.ROAD, "D-PX 4821", "Horse truck", 9, "DE-REG-2020-71203", "INS-DE-640918", "Air suspension; automatic watering; CCTV; temperature monitoring", "DE"),
        new("Renault Midlum Horse Box", TransportMode.ROAD, "TR-558-ME", "Horse truck", 2, "FR-REG-2018-56628", "INS-FR-775590", "Padded partitions; ventilation", "FR"),
        new("Boeing 747-400F Equine Cargo", TransportMode.AIR, null, "Cargo aircraft", 9, "NL-REG-2016-00418", "INS-NL-310072", "Air stalls; temperature and pressure monitoring; groom seating", "NL"),
        new("Boeing 737-800 Freighter, Horse Stalls", TransportMode.AIR, null, "Cargo aircraft", 4, "GB-REG-2017-00931", "INS-GB-208733", "Air stalls; temperature monitoring", "GB"),
        new("MV Channel Stallion", TransportMode.SEA, null, "Livestock vessel", 9, "GB-IMO-2015-91827", "INS-GB-209105", "Ventilated horse deck; fresh-water supply; CCTV", "GB"),
        new("MV Celtic Horse Carrier", TransportMode.SEA, null, "Livestock vessel", 7, "IE-IMO-2014-80364", "INS-IE-455219", "Ventilated horse deck; veterinary pen; temperature monitoring", "IE")
    ];

    // The addresses use the reserved .example domain so no demo provider can mail a real company.
    private static readonly ProviderProfile[] ProviderProfiles =
    [
        new("Equitrans Europeenne SAS", "Camille Laurent", "contact@equitrans-europeenne.example", "FR", "FR-VAT-100001", "ROAD", "Specialist in road transport of sport horses between France and the United Kingdom."),
        new("Channel Equine Logistics Ltd", "Oliver Hughes", "dispatch@channel-equine.example", "GB", "GB-VAT-100002", "ROAD,SEA", "Crosses the Channel daily via Dover and Eurotunnel with fully equipped horse lorries."),
        new("Emerald Horse Transport Ltd", "Aoife Brennan", "bookings@emerald-horse.example", "IE", "IE-VAT-100003", "ROAD,SEA", "Ireland to Great Britain and France, including ferry crossings from Rosslare."),
        new("Rhein Pferdetransport GmbH", "Jonas Keller", "info@rhein-pferdetransport.example", "DE", "DE-VAT-100004", "ROAD", "Long-distance road transport of warmbloods across Germany, the Netherlands and France."),
        new("Hollandse Paardenvervoer BV", "Daan de Vries", "planning@hollandse-paardenvervoer.example", "NL", "NL-VAT-100005", "ROAD,AIR", "Road transport to Schiphol and Liège for onward air freight of horses."),
        new("Flandres Equine Express BV", "Lotte Peeters", "office@flandres-equine.example", "BE", "BE-VAT-100006", "ROAD,SEA", "Belgian provider covering the Benelux and the Channel ports."),
        new("Skyline Equine Air Cargo", "Marcus Bell", "ops@skyline-equine-air.example", "GB", "GB-VAT-100007", "AIR", "Charter and scheduled air freight of horses from London Heathrow and East Midlands."),
        new("Atlantic Bloodstock Shipping", "Niamh Doyle", "shipping@atlantic-bloodstock.example", "IE", "IE-VAT-100008", "AIR,SEA", "Handles bloodstock shipments from Shannon and Dublin to Europe."),
        new("Normandie Haras Transport", "Julien Moreau", "contact@normandie-haras.example", "FR", "FR-VAT-100009", "ROAD,SEA", "Collections from Normandy studs with ferry connections through Calais and Caen-Ouistreham."),
        new("Iberia Horse Logistics SL", "Lucia Fernandez", "hola@iberia-horse-logistics.example", "ES", "ES-VAT-100010", "ROAD,AIR", "Moves horses between Spain, Portugal and the rest of Europe.")
    ];

    private sealed record TransportSeed(
        string TransportCode,
        string BookingCode,
        TransportStatus Status)
    {
        public Guid Id => StableGuid(TransportCode);
    }

    private sealed record TemplateSeed(
        string Code,
        string Name,
        DossierTemplateStatus Status,
        string[] RequirementCodes)
    {
        public Guid Id => StableGuid(Code);
    }

    private sealed record ComplianceRequirementSeed(
        string RequirementCode,
        string RequirementName,
        string Description,
        string? Country,
        ComplianceRequirementCategory Category,
        string IssuingAuthority,
        string? BcpCode,
        string LegalBasis,
        bool ValidityRequired,
        int? ValidityPeriodDays,
        ComplianceRequirementStatus Status);
}
