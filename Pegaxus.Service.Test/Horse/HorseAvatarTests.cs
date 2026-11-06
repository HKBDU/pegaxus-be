using Microsoft.EntityFrameworkCore;
using Pegaxus.Service.Exceptions;

using HorseServiceImpl = Pegaxus.Service.HorseService.Service;
using HorseRequest = Pegaxus.Service.HorseService.Request;

namespace Pegaxus.Service.Test.HorseTests;

public class HorseAvatarTests
{
    private static HorseRequest.CreateHorseRequest Request(string? avatarPublicId = null) => new()
    {
        HorseName = "Pegasus", Sex = "MALE", Breed = "Thoroughbred", DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-3)),
        Color = "Bay", CountryOfOrigin = "France", WeightKg = 500, HeightCm = 160,
        MicrochipNumber = "985123456789012", PassportNumber = "276098100123456", IdentificationNotes = "None",
        Avatar = avatarPublicId is null ? null : new() { PublicId = avatarPublicId }
    };

    [Fact]
    public async Task CreateHorse_WithAnAvatar_StoresItsConfirmedUrl_AndShowsItInListAndDetail()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddCustomerAsync(db);
        var media = new HorseMedia(); var service = new HorseServiceImpl(db, media);
        var publicId = $"avatars/{account.AccountId:D}/pegasus";

        var created = await service.CreateHorseAsync(account.AccountId, Request(publicId));

        Assert.Equal(account.AccountId, media.ConfirmedFor);
        Assert.Equal(HorseMedia.UrlFor(publicId), (await service.GetHorseAsync(account.AccountId, created.HorseId, false)).AvatarUrl);
        Assert.Equal(HorseMedia.UrlFor(publicId), (await service.GetCustomerHorsesAsync(account.AccountId, new())).Items.Single().AvatarUrl);
    }

    [Fact]
    public async Task CreateHorse_WithoutAnAvatar_HasNone()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddCustomerAsync(db);
        var service = new HorseServiceImpl(db, new HorseMedia());

        var created = await service.CreateHorseAsync(account.AccountId, Request());

        Assert.Null((await service.GetHorseAsync(account.AccountId, created.HorseId, false)).AvatarUrl);
    }

    [Fact]
    public async Task UpdateHorse_ReplacesTheAvatarWhenGiven_AndKeepsItWhenOmitted()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddCustomerAsync(db);
        var service = new HorseServiceImpl(db, new HorseMedia());
        var created = await service.CreateHorseAsync(account.AccountId, Request($"avatars/{account.AccountId:D}/first"));

        await service.UpdateHorseAsync(account.AccountId, created.HorseId, Request());
        Assert.Equal(HorseMedia.UrlFor($"avatars/{account.AccountId:D}/first"), (await service.GetHorseAsync(account.AccountId, created.HorseId, false)).AvatarUrl);

        await service.UpdateHorseAsync(account.AccountId, created.HorseId, Request($"avatars/{account.AccountId:D}/second"));
        Assert.Equal(HorseMedia.UrlFor($"avatars/{account.AccountId:D}/second"), (await service.GetHorseAsync(account.AccountId, created.HorseId, false)).AvatarUrl);
    }

    [Fact]
    public async Task CreateHorse_RefusesAnAvatarWhoseContentDoesNotMatchTheConfirmedUpload_AndSavesNothing()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddCustomerAsync(db);
        var service = new HorseServiceImpl(db, new HorseMedia { Content = [9, 9] });

        var error = await Assert.ThrowsAsync<BadRequestException>(() => service.CreateHorseAsync(account.AccountId, Request($"avatars/{account.AccountId:D}/bad")));

        Assert.Equal("INVALID_FILE", error.ErrorCode); Assert.Empty(await db.Horses.ToListAsync());
    }
}
