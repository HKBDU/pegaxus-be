using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Service.Exceptions;

using HorseServiceImpl = Pegaxus.Service.HorseService.Service;
using HorseRequest = Pegaxus.Service.HorseService.Request;

namespace Pegaxus.Service.Test.HorseTests;

public class HorseSoftDeleteReuseTests
{
    private static HorseRequest.CreateHorseRequest Request(string microchip = "985123456789012", string passport = "276098100123456") => new()
    {
        HorseName = "Pegasus", Sex = "MALE", Breed = "Thoroughbred", DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-3)),
        Color = "Bay", CountryOfOrigin = "France", WeightKg = 500, HeightCm = 160,
        MicrochipNumber = microchip, PassportNumber = passport, IdentificationNotes = "None"
    };

    [Fact]
    public async Task CreateHorse_AfterDeletingItWithTheSameNumbers_CreatesANewHorseAndKeepsTheOldOne()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddCustomerAsync(db);
        var service = new HorseServiceImpl(db, new HorseTests.HorseMedia());
        var first = await service.CreateHorseAsync(account.AccountId, Request());
        await service.DeleteHorseAsync(account.AccountId, first.HorseId);

        var second = await service.CreateHorseAsync(account.AccountId, Request());

        Assert.NotEqual(first.HorseId, second.HorseId);
        var old = await db.Horses.SingleAsync(item => item.HorseId == first.HorseId);
        Assert.True(old.IsDeleted); Assert.Equal("985123456789012", old.MicrochipNumber); Assert.Equal("276098100123456", old.PassportNumber);
    }

    [Fact]
    public async Task CreateHorse_WithTheNumbersOfALiveHorse_IsStillRefused()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddCustomerAsync(db);
        var service = new HorseServiceImpl(db, new HorseTests.HorseMedia());
        await service.CreateHorseAsync(account.AccountId, Request());

        var microchip = await Assert.ThrowsAsync<ConflictException>(() => service.CreateHorseAsync(account.AccountId, Request(passport: "276098100123999")));
        var passport = await Assert.ThrowsAsync<ConflictException>(() => service.CreateHorseAsync(account.AccountId, Request(microchip: "985123456789999")));

        Assert.Equal("DUPLICATE_MICROCHIP", microchip.ErrorCode); Assert.Equal("DUPLICATE_PASSPORT", passport.ErrorCode);
    }

    [Fact]
    public async Task CreateHorse_AnotherCustomerMayUseTheNumbersOfADeletedHorse()
    {
        await using var db = TestSupport.CreateDb();
        var (owner, _) = await TestSupport.AddCustomerAsync(db);
        var (buyer, _) = await TestSupport.AddCustomerAsync(db);
        var service = new HorseServiceImpl(db, new HorseTests.HorseMedia());
        var sold = await service.CreateHorseAsync(owner.AccountId, Request());
        await service.DeleteHorseAsync(owner.AccountId, sold.HorseId);

        var bought = await service.CreateHorseAsync(buyer.AccountId, Request());

        Assert.NotEqual(sold.HorseId, bought.HorseId);
    }

    [Fact]
    public async Task UpdateHorse_MayTakeTheNumbersOfADeletedHorse()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddCustomerAsync(db);
        var service = new HorseServiceImpl(db, new HorseTests.HorseMedia());
        var deleted = await service.CreateHorseAsync(account.AccountId, Request());
        await service.DeleteHorseAsync(account.AccountId, deleted.HorseId);
        var live = await service.CreateHorseAsync(account.AccountId, Request("985123456780000", "276098100120000"));

        await service.UpdateHorseAsync(account.AccountId, live.HorseId, Request());

        Assert.Equal("985123456789012", (await db.Horses.SingleAsync(item => item.HorseId == live.HorseId)).MicrochipNumber);
    }
}
