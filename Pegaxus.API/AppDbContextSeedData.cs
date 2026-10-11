using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using RouteEntity = Pegaxus.Repository.Entities.Route;

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
        await transaction.CommitAsync(cancellationToken);
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
            .Where(item => item.BookingCode.StartsWith("REQ-20261010-DEMO-"))
            .ToDictionaryAsync(item => item.BookingCode, cancellationToken);
        var transports = await SeedDemoTransportsAsync(db, bookings, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var plans = await SeedDemoTransportPlansAsync(db, transports, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await SeedDemoAssignmentsAsync(db, plans, employees, now, cancellationToken);

        var locations = await SeedDemoLocationsAsync(db, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var routes = await SeedDemoRoutesAsync(db, locations, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await SeedDemoRoutePlansAsync(db, plans, routes, employees, now, cancellationToken);

        var templates = await SeedDemoTemplatesAsync(db, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await SeedDemoDossiersAsync(db, transports, templates, horses, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

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
        var seeds = orderedCustomerEmails.SelectMany((customerEmail, customerIndex) => Enumerable.Range(1, 4).Select(number =>
            new HorseSeed(
                customerEmail,
                $"DEMO-C{customerIndex + 1}-HORSE-{number:000}",
                $"Pegaxus Demo Horse {customerIndex + 1}-{number}",
                number % 3 == 0 ? HorseSex.GELDING : number % 2 == 0 ? HorseSex.FEMALE : HorseSex.MALE,
                number % 2 == 0 ? "Arabian" : "Thoroughbred",
                $"98511200{customerIndex + 1:00}{number:000000}")))
            .ToArray();

        foreach (var seed in seeds)
        {
            var customer = customers[seed.CustomerEmail];
            var horse = await db.Horses.SingleOrDefaultAsync(item => item.MicrochipNumber == seed.MicrochipNumber, cancellationToken);
            if (horse is null)
            {
                horse = new Horse
                {
                    HorseId = seed.Id,
                    CustomerId = customer.CustomerId,
                    HorseName = seed.HorseName,
                    Sex = seed.Sex,
                    Breed = seed.Breed,
                    MicrochipNumber = seed.MicrochipNumber,
                    PassportNumber = $"PEGAXUS-{seed.Code}",
                    DateOfBirth = new DateOnly(2021, 1, 1).AddDays(seed.Id.GetHashCode() & 120),
                    Color = seed.Sex == HorseSex.MALE ? "Bay" : "Chestnut",
                    CountryOfOrigin = "FR",
                    WeightKg = 520,
                    HeightCm = 165,
                    IdentificationNotes = "Seeded demo horse for Swagger testing.",
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Horses.Add(horse);
            }
            result[seed.Code] = horse;
        }

        return result;
    }

    private static async Task SeedDemoBookingsAsync(
        AppDbContext db,
        IReadOnlyDictionary<string, Customer> customers,
        IReadOnlyDictionary<string, Employee> employees,
        IReadOnlyDictionary<string, Horse> horses,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var seeds = new[]
        {
            new BookingSeed("REQ-20261010-DEMO-0001", "trinhnganquan@gmail.com", BookingStatus.DRAFT, "FR", "FR", new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 22), "Draft booking for edit tests.", new[] { "DEMO-C1-HORSE-001", "DEMO-C1-HORSE-002" }),
            new BookingSeed("REQ-20261010-DEMO-0002", "batquyquan@gmail.com", BookingStatus.PENDING, "FR", "GB", new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 29), "Temperature monitoring required.", new[] { "DEMO-C2-HORSE-001", "DEMO-C2-HORSE-002" }),
            new BookingSeed("REQ-20261010-DEMO-0003", "kimvetuan@gmail.com", BookingStatus.APPROVED, "FR", "GB", new DateOnly(2026, 10, 27), new DateOnly(2026, 11, 1), "International approved booking without plan.", new[] { "DEMO-C3-HORSE-001", "DEMO-C3-HORSE-002", "DEMO-C3-HORSE-003" }),
            new BookingSeed("REQ-20261010-DEMO-0004", "khuyenvkse200481@fpt.edu.vn", BookingStatus.APPROVED, "DE", "FR", new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 7), "Approved booking with draft plan.", new[] { "DEMO-C4-HORSE-001", "DEMO-C4-HORSE-002" }),
            new BookingSeed("REQ-20261010-DEMO-0005", "trinhnganquan@gmail.com", BookingStatus.APPROVED, "FR", "GB", new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 6), "Approved booking ready for assignment and compliance.", new[] { "DEMO-C1-HORSE-003", "DEMO-C1-HORSE-004" }),
            new BookingSeed("REQ-20261010-DEMO-0006", "batquyquan@gmail.com", BookingStatus.REJECTED, "GB", "FR", new DateOnly(2026, 12, 10), new DateOnly(2026, 12, 15), "Rejected booking for negative-path tests.", new[] { "DEMO-C2-HORSE-003" })
        };

        foreach (var seed in seeds)
        {
            if (await db.Bookings.AnyAsync(item => item.BookingCode == seed.BookingCode, cancellationToken))
                continue;

            var customer = customers[seed.CustomerEmail];
            var booking = new Booking
            {
                BookingId = seed.Id,
                BookingCode = seed.BookingCode,
                CustomerId = customer.CustomerId,
                AssignedEmployeeId = employees["diechahien@gmail.com"].EmployeeId,
                Status = seed.Status,
                OriginAddress = seed.OriginCountry == "FR" ? "Haras de Chantilly, Paris" : "Newmarket Stables, London",
                OriginCountry = seed.OriginCountry,
                DestinationAddress = seed.DestinationCountry == "GB" ? "Newmarket Stables, London" : "Haras de Chantilly, Paris",
                DestinationCountry = seed.DestinationCountry,
                RequestedDepartureDate = seed.DepartureDate,
                RequestedArrivalDate = seed.ArrivalDate,
                SpecialRequirements = seed.SpecialRequirements,
                CustomerNote = "Seeded demo booking for Swagger testing.",
                SubmittedAt = seed.Status == BookingStatus.DRAFT ? null : now.AddDays(-5),
                ApprovedAt = seed.Status == BookingStatus.APPROVED ? now.AddDays(-3) : null,
                RejectedAt = seed.Status == BookingStatus.REJECTED ? now.AddDays(-3) : null,
                RejectionReason = seed.Status == BookingStatus.REJECTED ? "Demo rejection for negative-path testing." : null,
                CreatedAt = now,
                UpdatedAt = now
            };
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

    private static async Task<Dictionary<string, Transport>> SeedDemoTransportsAsync(
        AppDbContext db,
        IReadOnlyDictionary<string, Booking> bookings,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var seeds = new[]
        {
            new TransportSeed("TR-20261010-DEMO-0003", "REQ-20261010-DEMO-0003", TransportStatus.CREATED),
            new TransportSeed("TR-20261010-DEMO-0004", "REQ-20261010-DEMO-0004", TransportStatus.PLANNING),
            new TransportSeed("TR-20261010-DEMO-0005", "REQ-20261010-DEMO-0005", TransportStatus.READY)
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

    private static async Task<Dictionary<string, TransportPlan>> SeedDemoTransportPlansAsync(
        AppDbContext db,
        IReadOnlyDictionary<string, Transport> transports,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var seeds = new[]
        {
            new TransportPlanSeed("TP-20261010-DEMO-0004", "REQ-20261010-DEMO-0004", TransportPlanStatus.DRAFT),
            new TransportPlanSeed("TP-20261010-DEMO-0005", "REQ-20261010-DEMO-0005", TransportPlanStatus.APPROVED)
        };
        var result = new Dictionary<string, TransportPlan>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in seeds)
        {
            var plan = await db.TransportPlans.SingleOrDefaultAsync(item => item.PlanCode == seed.PlanCode, cancellationToken);
            if (plan is null)
            {
                plan = new TransportPlan
                {
                    TransportPlanId = seed.Id,
                    TransportId = transports[seed.BookingCode].TransportId,
                    PlanCode = seed.PlanCode,
                    Status = seed.Status,
                    PlannedStartAt = now.AddDays(20),
                    PlannedEndAt = now.AddDays(25),
                    PlanningNote = "Seeded overall transport plan for Swagger testing.",
                    EstimatedDistanceKm = 920.5m,
                    EstimatedDurationMinutes = 1260,
                    ApprovedAt = seed.Status == TransportPlanStatus.APPROVED ? now.AddDays(-1) : null,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.TransportPlans.Add(plan);
            }
            result[seed.BookingCode] = plan;
        }
        return result;
    }

    private static async Task SeedDemoAssignmentsAsync(
        AppDbContext db,
        IReadOnlyDictionary<string, TransportPlan> plans,
        IReadOnlyDictionary<string, Employee> employees,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var plan = plans["REQ-20261010-DEMO-0005"];
        var assignments = new[]
        {
            (Email: "huyvog6226@gmail.com", Responsibility: EmployeeRole.TRANSPORT_SPECIALIST.ToString(), IsPrimary: true),
            (Email: "hoanguyen91011@gmail.com", Responsibility: EmployeeRole.FLEET_ROUTE_COORDINATOR.ToString(), IsPrimary: true)
        };
        foreach (var assignment in assignments)
        {
            var employeeId = employees[assignment.Email].EmployeeId;
            var existing = await db.EmployeeTransportPlans.SingleOrDefaultAsync(item =>
                item.EmployeeId == employeeId && item.TransportPlanId == plan.TransportPlanId, cancellationToken);
            if (existing is not null)
            {
                if (existing.Responsibility is "Compliance dossier" or "Route planning")
                    existing.Responsibility = assignment.Responsibility;
                continue;
            }
            if (await db.EmployeeTransportPlans.AnyAsync(item => item.TransportPlanId == plan.TransportPlanId &&
                item.IsPrimary && item.Responsibility == assignment.Responsibility, cancellationToken))
                continue;
            db.EmployeeTransportPlans.Add(new EmployeeTransportPlan
            {
                EmployeeId = employeeId,
                TransportPlanId = plan.TransportPlanId,
                Responsibility = assignment.Responsibility,
                AssignedAt = now.AddDays(-1),
                IsPrimary = assignment.IsPrimary,
                Note = "Seeded demo assignment."
            });
        }
    }

    private static async Task<Dictionary<string, Location>> SeedDemoLocationsAsync(AppDbContext db, DateTime now, CancellationToken cancellationToken)
    {
        var seeds = new[]
        {
            new LocationSeed("LOC-DEMO-PARIS", "Paris Stable", "FR", "Paris"),
            new LocationSeed("LOC-DEMO-LONDON", "London Stable", "GB", "London"),
            new LocationSeed("LOC-DEMO-BERLIN", "Berlin Stable", "DE", "Berlin")
        };
        var result = new Dictionary<string, Location>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in seeds)
        {
            var location = await db.Locations.SingleOrDefaultAsync(item => item.LocationName == seed.Name, cancellationToken);
            if (location is null)
            {
                location = new Location
                {
                    LocationId = seed.Id,
                    LocationName = seed.Name,
                    LocationType = LocationType.STABLE,
                    Address = seed.Name,
                    City = seed.City,
                    Country = seed.Country,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Locations.Add(location);
            }
            result[seed.Code] = location;
        }
        return result;
    }

    private static async Task<Dictionary<string, RouteEntity>> SeedDemoRoutesAsync(AppDbContext db, IReadOnlyDictionary<string, Location> locations, DateTime now, CancellationToken cancellationToken)
    {
        var seeds = new[]
        {
            new RouteSeed("ROUTE-DEMO-FR-GB", "LOC-DEMO-PARIS", "LOC-DEMO-LONDON", "Paris - London horse transport", 920.5m, 1260),
            new RouteSeed("ROUTE-DEMO-DE-FR", "LOC-DEMO-BERLIN", "LOC-DEMO-PARIS", "Berlin - Paris horse transport", 1050m, 1440)
        };
        var result = new Dictionary<string, RouteEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in seeds)
        {
            var route = await db.Routes.SingleOrDefaultAsync(item => item.RouteCode == seed.Code, cancellationToken);
            if (route is null)
            {
                route = new RouteEntity
                {
                    RouteId = seed.Id,
                    RouteCode = seed.Code,
                    FromLocationId = locations[seed.From].LocationId,
                    ToLocationId = locations[seed.To].LocationId,
                    TransportMode = TransportMode.ROAD,
                    RouteName = seed.Name,
                    DistanceKm = seed.DistanceKm,
                    EstimatedDurationMinutes = seed.DurationMinutes,
                    RouteNote = "Seeded demo route.",
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Routes.Add(route);
            }
            result[seed.Code] = route;
        }
        return result;
    }

    private static async Task SeedDemoRoutePlansAsync(AppDbContext db, IReadOnlyDictionary<string, TransportPlan> plans, IReadOnlyDictionary<string, RouteEntity> routes, IReadOnlyDictionary<string, Employee> employees, DateTime now, CancellationToken cancellationToken)
    {
        var plan = plans["REQ-20261010-DEMO-0005"];
        if (!await db.RoutePlans.AnyAsync(item => item.RoutePlanCode == "RP-20261010-DEMO-0001", cancellationToken))
        {
            var routePlan = new RoutePlan
            {
                RoutePlanId = Guid.Parse("71000000-0000-0000-0000-000000000001"),
                TransportPlanId = plan.TransportPlanId,
                RoutePlanCode = "RP-20261010-DEMO-0001",
                Status = RoutePlanStatus.DRAFT,
                TotalDistanceKm = 920.5m,
                EstimatedDurationMinutes = 1260,
                RouteNote = "Primary route for Swagger testing.",
                CreatedAt = now,
                UpdatedAt = now
            };
            db.RoutePlans.Add(routePlan);
            db.RoutePlanRoutes.Add(new RoutePlanRoute
            {
                RoutePlanRouteId = Guid.Parse("72000000-0000-0000-0000-000000000001"),
                RoutePlanId = routePlan.RoutePlanId,
                RouteId = routes["ROUTE-DEMO-FR-GB"].RouteId,
                SequenceNo = 1,
                RouteType = RouteType.PRIMARY,
                DriverEmployeeId = employees["duongngo2209@gmail.com"].EmployeeId,
                EscortEmployeeId = employees["quocbao.nguyen16102006@gmail.com"].EmployeeId,
                PlannedDepartureAt = now.AddDays(20),
                PlannedArrivalAt = now.AddDays(25),
                Note = "Seeded primary route.",
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }

    private static async Task<Dictionary<string, DossierTemplate>> SeedDemoTemplatesAsync(AppDbContext db, DateTime now, CancellationToken cancellationToken)
    {
        var requirementIds = await db.ComplianceRequirements.ToDictionaryAsync(item => item.RequirementCode, item => item.ComplianceRequirementId, cancellationToken);
        var seeds = new[]
        {
            new TemplateSeed("TPL-DEMO-EU-GB", "EU to GB Equine Transport", DossierTemplateStatus.ACTIVE, new[] { "EQUINE_PASSPORT", "EHC_EU_TO_GB", "IPAFFS_PRE_NOTIF", "ATA_CARNET_EXPORT" }),
            new TemplateSeed("TPL-DEMO-DRAFT", "Draft Demo Template", DossierTemplateStatus.DRAFT, new[] { "EQUINE_PASSPORT", "JOURNEY_LOG_EC1_2005" })
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
                    Description = "Seeded demo dossier template for Swagger testing.",
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
        var transport = transports["REQ-20261010-DEMO-0005"];
        if (await db.ComplianceDossiers.AnyAsync(item => item.DossierCode == "CDOS-20261010-DEMO-0001", cancellationToken))
            return;

        var requirements = await db.ComplianceRequirements
            .Where(item => item.RequirementCode == "EQUINE_PASSPORT" || item.RequirementCode == "EHC_EU_TO_GB" || item.RequirementCode == "IPAFFS_PRE_NOTIF")
            .ToDictionaryAsync(item => item.RequirementCode, cancellationToken);
        var dossier = new ComplianceDossier
        {
            ComplianceDossierId = Guid.Parse("73000000-0000-0000-0000-000000000001"),
            DossierCode = "CDOS-20261010-DEMO-0001",
            TransportId = transport.TransportId,
            DossierTemplateId = templates["TPL-DEMO-EU-GB"].DossierTemplateId,
            Status = ComplianceDossierStatus.IN_PREPARATION,
            OpenedAt = now.AddDays(-1),
            Note = "Seeded demo dossier for Swagger testing.",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ComplianceDossiers.Add(dossier);
        db.DossierRequirements.AddRange(
            new DossierRequirement
            {
                DossierRequirementId = Guid.Parse("74000000-0000-0000-0000-000000000001"),
                ComplianceDossierId = dossier.ComplianceDossierId,
                ComplianceRequirementId = requirements["EQUINE_PASSPORT"].ComplianceRequirementId,
                HorseId = horses["DEMO-C1-HORSE-003"].HorseId,
                Status = DossierRequirementStatus.PROVIDED,
                IsMandatory = true,
                DueDate = new DateOnly(2026, 11, 20),
                CreatedAt = now,
                UpdatedAt = now
            },
            new DossierRequirement
            {
                DossierRequirementId = Guid.Parse("74000000-0000-0000-0000-000000000002"),
                ComplianceDossierId = dossier.ComplianceDossierId,
                ComplianceRequirementId = requirements["EHC_EU_TO_GB"].ComplianceRequirementId,
                Status = DossierRequirementStatus.PENDING,
                IsMandatory = true,
                DueDate = new DateOnly(2026, 11, 25),
                CreatedAt = now,
                UpdatedAt = now
            },
            new DossierRequirement
            {
                DossierRequirementId = Guid.Parse("74000000-0000-0000-0000-000000000003"),
                ComplianceDossierId = dossier.ComplianceDossierId,
                ComplianceRequirementId = requirements["IPAFFS_PRE_NOTIF"].ComplianceRequirementId,
                Status = DossierRequirementStatus.REQUESTED,
                IsMandatory = true,
                DueDate = new DateOnly(2026, 11, 28),
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
        return new Guid(guidBytes);
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

    private static async Task<string> GenerateCustomerCodeAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = $"CUS-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(0, 10_000):D4}";
            if (!await db.Customers.AnyAsync(item => item.CustomerCode == code, cancellationToken))
                return code;
        }

        throw new InvalidOperationException("Could not generate a unique customer code.");
    }

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
        new("EQUINE_PASSPORT", "Equine Passport", "Hộ chiếu định danh ngựa, dùng để đối chiếu tên ngựa, số hộ chiếu và microchip.", "EU", ComplianceRequirementCategory.HorseIdentity, "Weatherbys / France Galop / Approved Passport Issuing Organisation", null, "Equine identification and passport control requirements.", true, 365, ComplianceRequirementStatus.ACTIVE),
        new("EHC_GB_TO_EU_8438", "Export Health Certificate GB to EU — Form 8438", "Chứng thư kiểm dịch thú y xuất khẩu từ GB sang EU.", "GB", ComplianceRequirementCategory.HorseHealth, "APHA / Official Veterinarian", null, "Export health certification for equidae transported from GB to EU.", true, 10, ComplianceRequirementStatus.ACTIVE),
        new("EHC_EU_TO_GB", "Export Health Certificate EU to GB", "Chứng thư kiểm dịch thú y xuất khẩu từ EU vào GB.", "EU", ComplianceRequirementCategory.HorseHealth, "EU Official Veterinarian / TRACES-NT", null, "Export health certification for equidae transported from EU to GB.", true, 10, ComplianceRequirementStatus.ACTIVE),
        new("LAB_TEST_EIA_COGGINS", "Laboratory Test Report — EIA Coggins Test", "Kết quả xét nghiệm Coggins/EIA của ngựa, dùng để chứng minh kết quả âm tính khi tuyến yêu cầu.", null, ComplianceRequirementCategory.HorseHealth, "Approved Veterinary Laboratory", null, "Equine infectious anaemia testing requirement for applicable routes.", true, 90, ComplianceRequirementStatus.ACTIVE),
        new("CHED_A_DOCUMENT", "CHED-A Animal Entry Document", "Tờ khai y tế nhập cảnh động vật trên hệ thống TRACES-NT cho tuyến có kiểm soát BCP.", "EU", ComplianceRequirementCategory.Quarantine, "TRACES-NT / EU Border Control Post", "FRCAL1", "Common Health Entry Document for Animals.", true, 1, ComplianceRequirementStatus.ACTIVE),
        new("IPAFFS_PRE_NOTIF", "IPAFFS Import Pre-notification", "Khai báo trước khi đưa ngựa vào GB qua hệ thống IPAFFS.", "GB", ComplianceRequirementCategory.Quarantine, "UK Defra IPAFFS", "GBHOL1", "Pre-notification requirement for applicable imports into GB.", true, 1, ComplianceRequirementStatus.ACTIVE),
        new("ATA_CARNET_EXPORT", "ATA Carnet — Export Voucher", "Sổ ATA Carnet phục vụ thủ tục tạm xuất ngựa và tài sản liên quan.", "GB", ComplianceRequirementCategory.Customs, "Chamber of Commerce / Customs Authority", null, "Temporary admission and re-export customs procedure.", true, 365, ComplianceRequirementStatus.ACTIVE),
        new("ATA_CARNET_IMPORT", "ATA Carnet — Import Voucher", "Phần chứng từ ATA Carnet phục vụ thủ tục tạm nhập tại nước đến.", "EU", ComplianceRequirementCategory.Customs, "Chamber of Commerce / Customs Authority", null, "Temporary admission customs procedure at destination country.", true, 365, ComplianceRequirementStatus.ACTIVE),
        new("JOURNEY_LOG_EC1_2005", "EU Animal Transport Journey Log", "Nhật ký hành trình ghi nhận kế hoạch, thời gian di chuyển, thời gian nghỉ và các bất thường.", "EU", ComplianceRequirementCategory.Transport, "Transport Operator / Competent Authority", null, "Animal welfare and journey log requirements for transported equidae.", false, null, ComplianceRequirementStatus.ACTIVE),
        new("DRIVER_COMPETENCE_CERT", "Driver Competence Certificate", "Chứng chỉ năng lực của tài xế vận chuyển động vật sống.", null, ComplianceRequirementCategory.Transport, "Competent Transport Authority / Approved Training Body", null, "Driver competence requirement for transport of live animals.", true, 365, ComplianceRequirementStatus.INACTIVE)
    ];

    private sealed record EmployeeSeed(
        string FullName,
        string EmployeeCode,
        string Email,
        string Phone,
        EmployeeRole Role);

    private sealed record CustomerSeed(string FullName, string Email);

    private sealed record HorseSeed(
        string CustomerEmail,
        string Code,
        string HorseName,
        HorseSex Sex,
        string Breed,
        string MicrochipNumber)
    {
        public Guid Id => StableGuid(Code);
    }

    private sealed record BookingSeed(
        string BookingCode,
        string CustomerEmail,
        BookingStatus Status,
        string OriginCountry,
        string DestinationCountry,
        DateOnly DepartureDate,
        DateOnly ArrivalDate,
        string SpecialRequirements,
        string[] HorseCodes)
    {
        public Guid Id => StableGuid(BookingCode);
        public string? RejectionReason => Status == BookingStatus.REJECTED ? SpecialRequirements : null;
    }

    private sealed record TransportSeed(
        string TransportCode,
        string BookingCode,
        TransportStatus Status)
    {
        public Guid Id => StableGuid(TransportCode);
    }

    private sealed record TransportPlanSeed(
        string PlanCode,
        string BookingCode,
        TransportPlanStatus Status)
    {
        public Guid Id => StableGuid(PlanCode);
    }

    private sealed record LocationSeed(string Code, string Name, string Country, string City)
    {
        public Guid Id => StableGuid(Code);
    }

    private sealed record RouteSeed(
        string Code,
        string From,
        string To,
        string Name,
        decimal DistanceKm,
        int DurationMinutes)
    {
        public Guid Id => StableGuid(Code);
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
