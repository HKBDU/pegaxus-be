using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Pegaxus.Service.MediaService;

namespace Pegaxus.Service.CloudinaryService;

public class Service:IService
{
    private readonly Cloudinary _cloudinary;
    private readonly CloudinaryOptions _cloudinaryOptions = new();

    public Service(IConfiguration configuration)
    {
        configuration.GetSection(nameof(CloudinaryOptions)).Bind(_cloudinaryOptions);
        _cloudinary = new Cloudinary(new Account(
            _cloudinaryOptions.CloudName,
            _cloudinaryOptions.ApiKey,
            _cloudinaryOptions.ApiSecret));
    }

    public async Task<string> UploadImageAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            throw new ArgumentException("File is empty or null.", nameof(file));
        }
        if (!IsImageFile(file))
        {
            throw new ArgumentException("File is not a valid image.", nameof(file));
        }
        
        await using var stream = file.OpenReadStream();
        var uploadParams = new ImageUploadParams()
        {
            File = new FileDescription(file.FileName, stream)
        };
        var uploadResult = await _cloudinary.UploadAsync(uploadParams);
        return uploadResult.SecureUrl.ToString();
    }
    
    private bool IsImageFile(IFormFile file)
    {
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif",".webp" };
        var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
        return allowedExtensions.Contains(fileExtension);
    }
    
    // UploadFileAsync: upload PDF, DOCX... lên Cloudinary dùng RawUploadParams
    public async Task<(string FileUrl, string PublicId)> UploadFileAsync(IFormFile file)
    {
        await using var stream = file.OpenReadStream();
 
        var uploadParams = new RawUploadParams
        {
            File           = new FileDescription(file.FileName, stream),
            Folder         = "smartcenter/documents",
            UseFilename    = true,
            UniqueFilename = true,
        };
 
        var uploadResult = await _cloudinary.UploadAsync(uploadParams);
 
        if (uploadResult.Error != null)
            throw new Exception($"Cloudinary upload failed: {uploadResult.Error.Message}");
 
        return (uploadResult.SecureUrl.ToString(), uploadResult.PublicId);
    }
 
    // DeleteFileAsync: xóa file khỏi Cloudinary theo publicId
    public async Task DeleteFileAsync(string publicId)
    {
        var deleteParams = new DeletionParams(publicId)
        {
            ResourceType = ResourceType.Raw
        };
 
        var result = await _cloudinary.DestroyAsync(deleteParams);
 
        if (result.Error != null)
            throw new Exception($"Cloudinary delete failed: {result.Error.Message}");
    }
}