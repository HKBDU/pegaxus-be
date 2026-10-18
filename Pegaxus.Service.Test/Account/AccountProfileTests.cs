using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.MediaService;

using AccountRequest = Pegaxus.Service.AccountService.Request;
using AccountServiceImpl = Pegaxus.Service.AccountService.Service;

namespace Pegaxus.Service.Test;

public class AccountProfileTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgo=");

    [Fact]
    public async Task Avatar_IsConfirmedFromAPresignedUploadAndStoredAsItsPublicUrl()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddCustomerAsync(db);
        var media = new AvatarMedia();

        var profile = await Service(db, media).UpdateProfileAsync(account.AccountId, new AccountRequest.UpdateProfileRequest
        { Avatar = new() { PublicId = $"avatars/{account.AccountId:D}/new" } });

        Assert.Equal(AvatarMedia.UrlFor($"avatars/{account.AccountId:D}/new"), profile.AvatarUrl);
        Assert.Equal(profile.AvatarUrl, (await db.Accounts.SingleAsync(item => item.AccountId == account.AccountId)).AvatarUrl);
    }

    [Fact]
    public async Task Avatar_WhoseContentDoesNotMatchTheConfirmedMetadataIsRejected()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddCustomerAsync(db);
        var media = new AvatarMedia { DownloadedContent = [1, 2, 3] };

        var error = await Assert.ThrowsAsync<BadRequestException>(() => Service(db, media).UpdateProfileAsync(account.AccountId,
            new AccountRequest.UpdateProfileRequest { FullName = "Changed", Avatar = new() { PublicId = $"avatars/{account.AccountId:D}/bad" } }));

        Assert.Equal("INVALID_FILE", error.ErrorCode);
        Assert.NotEqual("Changed", (await db.Customers.SingleAsync()).FullName);
    }

    [Fact]
    public async Task EmployeeCountry_MustBeAnIsoCodeAndIsStoredUpperCase()
    {
        await using var db = TestSupport.CreateDb();
        var (account, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        var service = Service(db, new AvatarMedia());

        await service.UpdateProfileAsync(account.AccountId, new AccountRequest.UpdateProfileRequest { Country = " fr " });
        Assert.Equal("FR", (await db.Employees.SingleAsync(item => item.EmployeeId == employee.EmployeeId)).Country);
        var error = await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateProfileAsync(account.AccountId,
            new AccountRequest.UpdateProfileRequest { Country = "France" }));
        Assert.Equal("INVALID_COUNTRY", error.ErrorCode);
    }

    [Fact]
    public async Task CustomerCountry_KeepsFreeText()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddCustomerAsync(db);

        await Service(db, new AvatarMedia()).UpdateProfileAsync(account.AccountId, new AccountRequest.UpdateProfileRequest { Country = " Viet Nam " });

        Assert.Equal("Viet Nam", (await db.Customers.SingleAsync()).Country);
    }

    private static AccountServiceImpl Service(Pegaxus.Repository.AppDbContext db, IMediaService media) => new(db, media, new PasswordHasher<Account>());

    private sealed class AvatarMedia : IMediaService
    {
        public static string UrlFor(string publicId) => $"https://res.cloudinary.com/test/image/upload/v1/{publicId}.png";
        public byte[]? DownloadedContent { get; init; }
        public Task<ConfirmedUploadResult> ConfirmAvatarAsync(Guid accountId, string publicId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConfirmedUploadResult(publicId, UrlFor(publicId), "new.png", "image/png", Png.Length));
        public Task<PreviewFile> DownloadPreviewAsync(string fileUrl, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PreviewFile(DownloadedContent ?? Png, "image/png"));
        public Task<string> UploadPrivateImageAsync(Guid imageId, PreviewFile image, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeletePrivateImageAsync(Guid imageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SignedUploadResult> CreateSignedUploadAsync(Guid accountId, string entity, string contentType, long sizeBytes) => throw new NotSupportedException();
        public Task<ConfirmedUploadResult> ConfirmUploadAsync(Guid accountId, string publicId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Pegaxus.Service.DocumentStorageService.SignedDocumentUrl> CreateReadUrlAsync(string fileUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
