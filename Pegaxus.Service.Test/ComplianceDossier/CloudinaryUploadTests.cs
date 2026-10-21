using System.Net;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Pegaxus.Service.Exceptions;

using CloudinaryUploadService = Pegaxus.Service.CloudinaryService.Service;

namespace Pegaxus.Service.Test;

public class CloudinaryUploadTests
{
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly string PublicId = $"evidence/{AccountId:D}/{Guid.NewGuid():N}";

    [Fact]
    public async Task Signature_SupportsFiveMegabytesAndPreventsPublicDeliveryOrOverwrite()
    {
        var result = await new TestCloudinary().CreateSignedUploadAsync(AccountId, "attachment", "application/pdf", 5 * 1024 * 1024);
        Assert.StartsWith($"evidence/{AccountId:D}/", result.PublicId);
        Assert.Equal("authenticated", result.Parameters["type"]);
        Assert.Equal("false", result.Parameters["overwrite"]);
        Assert.NotEmpty(result.Parameters["signature"]);
        Assert.DoesNotContain("test-secret", string.Join(',', result.Parameters.Values));
    }

    [Fact]
    public async Task Signature_RejectsFilesLargerThanFiveMegabytes()
    {
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() =>
            new TestCloudinary().CreateSignedUploadAsync(AccountId, "attachment", "application/pdf", 5 * 1024 * 1024 + 1));
        Assert.Equal(413, error.StatusCode);
        Assert.Equal("FILE_TOO_LARGE", error.ErrorCode);
    }

    [Fact]
    public async Task Confirmation_ResolvesTrustedMetadataAndSignsAnExpiringDownloadUrl()
    {
        var service = new TestCloudinary();
        var confirmed = await service.ConfirmUploadAsync(AccountId, PublicId);
        Assert.Equal("passport.pdf", confirmed.FileName);
        Assert.Equal("application/pdf", confirmed.MimeType);
        Assert.Equal(123, confirmed.SizeBytes);
        Assert.DoesNotContain("signature", confirmed.FileUrl);
        var signed = await service.CreateReadUrlAsync(confirmed.FileUrl);
        var uri = new Uri(signed.Url);
        var query = QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal("https", uri.Scheme);
        Assert.Equal("api.cloudinary.com", uri.Host);
        Assert.EndsWith("/image/download", uri.AbsolutePath);
        Assert.Equal(PublicId, query["public_id"].ToString());
        Assert.Equal("authenticated", query["type"].ToString());
        Assert.Equal(signed.ExpiresAt.ToUnixTimeSeconds().ToString(), query["expires_at"].ToString());
        Assert.NotEmpty(query["signature"].ToString());
        Assert.InRange(signed.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));
    }

    [Theory]
    [InlineData("foreign-owner")]
    [InlineData("../other")]
    [InlineData("%2e%2e/other")]
    [InlineData("")]
    public async Task InvalidOwnershipOrPublicId_IsRejectedBeforeCloudinaryAccess(string suffix)
    {
        var service = new TestCloudinary();
        var publicId = suffix == "foreign-owner" ? $"evidence/{Guid.NewGuid():D}/file" : $"evidence/{AccountId:D}/{suffix}";
        var error = await Assert.ThrowsAsync<BadRequestException>(() => service.ConfirmUploadAsync(AccountId, publicId));
        Assert.Equal("INVALID_PUBLIC_ID", error.ErrorCode);
        Assert.Equal(0, service.Calls);
    }

    [Theory]
    [InlineData("oversized", 413)]
    [InlineData("empty", 413)]
    [InlineData("format", 400)]
    [InlineData("public", 400)]
    [InlineData("foreign-url", 400)]
    [InlineData("wrong-id", 400)]
    [InlineData("unavailable", 502)]
    public async Task UntrustedOrIneligibleResourceMetadata_IsRejected(string invalid, int status)
    {
        var resource = Resource();
        switch (invalid)
        {
            case "oversized": resource.Bytes = 5 * 1024 * 1024 + 1; break;
            case "empty": resource.Bytes = 0; break;
            case "format": resource.Format = "exe"; break;
            case "public": resource.Type = "upload"; break;
            case "foreign-url": resource.SecureUrl = "https://attacker.example/file.pdf"; break;
            case "wrong-id": resource.PublicId = "another-file"; break;
            case "unavailable": resource.Error = new Error { Message = "sensitive" }; resource.StatusCode = HttpStatusCode.InternalServerError; break;
        }
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => new TestCloudinary(resource).ConfirmUploadAsync(AccountId, PublicId));
        Assert.Equal(status, error.StatusCode);
        Assert.DoesNotContain("sensitive", error.Message);
    }

    [Fact]
    public async Task CallerCancellation_IsForwardedToCloudinary()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TestCloudinary().ConfirmUploadAsync(AccountId, PublicId, cancellation.Token));
    }

    [Fact]
    public async Task MissingImage_FallsBackToRawAndMissingBothReturnsNotFound()
    {
        var rawId = PublicId + ".pdf";
        var raw = Resource("raw");
        raw.PublicId = rawId;
        raw.SecureUrl = $"https://res.cloudinary.com/test-cloud/raw/authenticated/v1/{rawId}";
        var service = new TestCloudinary(raw, missingImage: true);
        Assert.Equal(rawId, (await service.ConfirmUploadAsync(AccountId, rawId)).PublicId);
        Assert.Equal(2, service.Calls);
        var signed = await service.CreateReadUrlAsync(raw.SecureUrl);
        var uri = new Uri(signed.Url);
        Assert.EndsWith("/raw/download", uri.AbsolutePath);
        Assert.Equal(rawId, QueryHelpers.ParseQuery(uri.Query)["public_id"].ToString());
        raw.StatusCode = HttpStatusCode.NotFound;
        var error = await Assert.ThrowsAsync<NotFoundException>(() => new TestCloudinary(raw, missingImage: true).ConfirmUploadAsync(AccountId, rawId));
        Assert.Equal("UPLOAD_NOT_FOUND", error.ErrorCode);
    }

    [Fact]
    public async Task AvatarSignature_IsPublicLimitedToTwoMegabytesAndScopedToTheAccount()
    {
        var result = await new TestCloudinary().CreateSignedUploadAsync(AccountId, " Avatar ", "image/png", 2 * 1024 * 1024);
        Assert.StartsWith($"avatars/{AccountId:D}/", result.PublicId);
        Assert.Equal("upload", result.Parameters["type"]);
        Assert.Equal("jpg,jpeg,png", result.Parameters["allowed_formats"]);
        Assert.Equal("false", result.Parameters["overwrite"]);
        var tooLarge = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() =>
            new TestCloudinary().CreateSignedUploadAsync(AccountId, "avatar", "image/png", 2 * 1024 * 1024 + 1));
        Assert.Equal(413, tooLarge.StatusCode);
        var pdf = await Assert.ThrowsAsync<BadRequestException>(() => new TestCloudinary().CreateSignedUploadAsync(AccountId, "avatar", "application/pdf", 1000));
        Assert.Equal("INVALID_FILE_TYPE", pdf.ErrorCode);
        var entity = await Assert.ThrowsAsync<BadRequestException>(() => new TestCloudinary().CreateSignedUploadAsync(AccountId, "banner", "image/png", 1000));
        Assert.Equal("INVALID_UPLOAD_ENTITY", entity.ErrorCode);
    }

    [Fact]
    public async Task AvatarConfirmation_ReturnsThePublicUrlAndReadUrlsAreNotSigned()
    {
        var avatarId = $"avatars/{AccountId:D}/{Guid.NewGuid():N}";
        var service = new TestAvatarCloudinary(AvatarResource(avatarId));
        var confirmed = await service.ConfirmAvatarAsync(AccountId, avatarId);
        Assert.Equal("image/png", confirmed.MimeType);
        Assert.Equal(1000, confirmed.SizeBytes);
        Assert.Equal($"https://res.cloudinary.com/test-cloud/image/upload/v1/{avatarId}.png", confirmed.FileUrl);
        Assert.Equal(confirmed.FileUrl, (await service.CreateReadUrlAsync(confirmed.FileUrl)).Url);
    }

    [Theory]
    [InlineData("foreign-owner", 400)]
    [InlineData("authenticated", 400)]
    [InlineData("oversized", 413)]
    [InlineData("format", 400)]
    [InlineData("missing", 404)]
    public async Task AvatarConfirmation_RejectsIneligibleResources(string invalid, int status)
    {
        var avatarId = $"avatars/{AccountId:D}/{Guid.NewGuid():N}";
        var resource = AvatarResource(avatarId);
        var requested = avatarId;
        switch (invalid)
        {
            case "foreign-owner": requested = $"avatars/{Guid.NewGuid():D}/file"; break;
            case "authenticated": resource.Type = "authenticated"; break;
            case "oversized": resource.Bytes = 2 * 1024 * 1024 + 1; break;
            case "format": resource.Format = "pdf"; break;
            case "missing": resource.StatusCode = HttpStatusCode.NotFound; break;
        }
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => new TestAvatarCloudinary(resource).ConfirmAvatarAsync(AccountId, requested));
        Assert.Equal(status, error.StatusCode);
    }

    private static GetResourceResult AvatarResource(string publicId) =>
        JsonConvert.DeserializeObject<GetResourceResult>(JsonConvert.SerializeObject(new
        {
            public_id = publicId, format = "png", bytes = 1000, type = "upload", resource_type = "image",
            secure_url = $"https://res.cloudinary.com/test-cloud/image/upload/v1/{publicId}.png"
        }))!;

    internal sealed class TestAvatarCloudinary(GetResourceResult resource) : CloudinaryUploadService(Configuration(), new NoNetworkFactory())
    {
        protected override Task<GetResourceResult> GetResourceAsync(GetResourceParams parameters, CancellationToken cancellationToken)
        {
            Assert.Equal("upload", parameters.Type);
            Assert.Equal(ResourceType.Image, parameters.ResourceType);
            return Task.FromResult(resource);
        }
    }

    internal static GetResourceResult Resource(string resourceType = "image") =>
        JsonConvert.DeserializeObject<GetResourceResult>(JsonConvert.SerializeObject(new
        {
            public_id = PublicId, format = "pdf", bytes = 123, original_filename = "passport",
            type = "authenticated", resource_type = resourceType,
            secure_url = $"https://res.cloudinary.com/test-cloud/{resourceType}/authenticated/v1/{PublicId}.pdf"
        }))!;

    internal sealed class TestCloudinary(GetResourceResult? resource = null, bool missingImage = false)
        : CloudinaryUploadService(Configuration(), new NoNetworkFactory())
    {
        public int Calls { get; private set; }
        protected override Task<GetResourceResult> GetResourceAsync(GetResourceParams parameters, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Assert.Equal("authenticated", parameters.Type);
            return Task.FromResult(missingImage && parameters.ResourceType == ResourceType.Image
                ? new GetResourceResult { StatusCode = HttpStatusCode.NotFound }
                : resource ?? Resource());
        }
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["CloudinaryOptions:CloudName"] = "test-cloud", ["CloudinaryOptions:ApiKey"] = "test-key",
        ["CloudinaryOptions:ApiSecret"] = "test-secret"
    }).Build();

    private sealed class NoNetworkFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("No network access expected.");
    }
}
