namespace Pegaxus.Service.HorseHealthLogService;

public interface IHorseHealthLogService
{
    Task<Response.CreateResult> CreateAsync(Guid transportId, Request.CreateRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.BatchResult> MarkAllNormalAsync(Guid transportId, Request.RecordRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.PhotosResponse> AddPhotosAsync(Guid transportId, Guid horseHealthLogId, Request.AddPhotosRequest request, Guid accountId, CancellationToken cancellationToken = default);
}
