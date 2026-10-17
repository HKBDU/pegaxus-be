using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.MediaService;

using CloudinaryStorage = Pegaxus.Service.CloudinaryService.Service;

namespace Pegaxus.Service.Test;

public class PrivateHealthImageTests
{
    [Fact]
    public async Task Upload_UsesAuthenticatedImageWithoutOverwriteAndSignsPrivateDownload()
    {
        var service = new Storage(); var id = Guid.NewGuid();
        var url = await service.UploadPrivateImageAsync(id, new(HorseHealthLogTests.Png, "image/png"));
        Assert.Equal(HorseHealthLogTests.Storage.Url(id), url);
        var parameters = Assert.IsType<ImageUploadParams>(service.Upload);
        Assert.Equal($"health/{id:N}", parameters.PublicId); Assert.Equal("authenticated", parameters.Type); Assert.False(parameters.Overwrite);
        var signed = await service.CreateReadUrlAsync(url);
        var query = QueryHelpers.ParseQuery(new Uri(signed.Url).Query);
        Assert.Equal($"health/{id:N}", query["public_id"].ToString()); Assert.Equal("authenticated", query["type"].ToString());
        Assert.NotEmpty(query["signature"].ToString()); Assert.True(signed.ExpiresAt > DateTimeOffset.UtcNow);
        await service.DeletePrivateImageAsync(id);
        Assert.Equal($"health/{id:N}", service.Deletion!.PublicId);
        Assert.Equal(ResourceType.Image, service.Deletion.ResourceType); Assert.Equal("authenticated", service.Deletion.Type);
    }

    [Theory]
    [InlineData("emptyId")]
    [InlineData("empty")]
    [InlineData("signature")]
    [InlineData("type")]
    [InlineData("oversized")]
    public async Task Upload_RejectsInvalidImagesBeforeProviderCall(string scenario)
    {
        var service = new Storage(); byte[] bytes = scenario switch { "empty" => [], "signature" => "bad image"u8.ToArray(),
            "oversized" => new byte[5 * 1024 * 1024 + 1], _ => HorseHealthLogTests.Png };
        var image = new PreviewFile(bytes, scenario == "type" ? "application/pdf" : "image/png");
        Assert.Equal("INVALID_IMAGE", (await Assert.ThrowsAsync<BadRequestException>(() =>
            service.UploadPrivateImageAsync(scenario == "emptyId" ? Guid.Empty : Guid.NewGuid(), image))).ErrorCode);
        Assert.Null(service.Upload);
    }

    [Theory]
    [InlineData("public")]
    [InlineData("foreignHost")]
    [InlineData("foreignId")]
    [InlineData("error")]
    public async Task Upload_RejectsInvalidProviderResponse(string scenario)
    {
        var id = Guid.NewGuid(); var service = new Storage { Response = JsonConvert.DeserializeObject<ImageUploadResult>(JsonConvert.SerializeObject(new
        {
            public_id = scenario == "foreignId" ? "other/id" : $"health/{id:N}",
            secure_url = scenario == "foreignHost" ? "https://example.com/photo.png" :
                $"https://res.cloudinary.com/test-cloud/image/{(scenario == "public" ? "upload" : "authenticated")}/health/{id:N}.png",
            error = scenario == "error" ? new { message = "Provider failed" } : null
        })) };
        var error = await Assert.ThrowsAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.UploadPrivateImageAsync(id, new(HorseHealthLogTests.Png, "image/png")));
        Assert.Equal(502, error.StatusCode); Assert.Equal("FILE_STORAGE_UNAVAILABLE", error.ErrorCode);
    }

    [Fact]
    public async Task UploadAndCleanup_RespectCancellationBeforeProviderCall()
    {
        var service = new Storage(); using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.UploadPrivateImageAsync(Guid.NewGuid(), new(HorseHealthLogTests.Png, "image/png"), canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DeletePrivateImageAsync(Guid.NewGuid(), canceled.Token));
        Assert.Null(service.Upload); Assert.Null(service.Deletion);
    }

    [Theory]
    [InlineData("ok", true)]
    [InlineData("not found", true)]
    [InlineData("failed", false)]
    public async Task Cleanup_AcceptsOnlyConfirmedDeletionOrMissingImage(string result, bool allowed)
    {
        var service = new Storage { DeleteResult = result };
        if (allowed) await service.DeletePrivateImageAsync(Guid.NewGuid());
        else Assert.Equal("FILE_STORAGE_UNAVAILABLE", (await Assert.ThrowsAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.DeletePrivateImageAsync(Guid.NewGuid()))).ErrorCode);
    }

    private sealed class Storage() : CloudinaryStorage(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["CloudinaryOptions:CloudName"] = "test-cloud", ["CloudinaryOptions:ApiKey"] = "test-key", ["CloudinaryOptions:ApiSecret"] = "test-secret"
    }).Build(), new NoNetwork())
    {
        internal ImageUploadParams? Upload { get; private set; }
        internal DeletionParams? Deletion { get; private set; }
        internal ImageUploadResult? Response { get; init; }
        internal string DeleteResult { get; init; } = "ok";
        protected override Task<ImageUploadResult> UploadPrivateImageCoreAsync(ImageUploadParams parameters, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Upload = parameters;
            return Task.FromResult(Response ?? JsonConvert.DeserializeObject<ImageUploadResult>(JsonConvert.SerializeObject(new
            { public_id = parameters.PublicId, secure_url = $"https://res.cloudinary.com/test-cloud/image/authenticated/{parameters.PublicId}.png" }))!);
        }
        protected override Task<DeletionResult> DeletePrivateImageCoreAsync(DeletionParams parameters, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Deletion = parameters;
            return Task.FromResult(JsonConvert.DeserializeObject<DeletionResult>(JsonConvert.SerializeObject(new { result = DeleteResult }))!);
        }
    }

    private sealed class NoNetwork : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Tests must not contact image storage.");
    }
}
