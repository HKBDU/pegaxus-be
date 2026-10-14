namespace Pegaxus.Service.HorseService;

public interface IHorseService
{
    Task<Response.HorseListResponse> GetCustomerHorsesAsync(
        Guid accountId,
        Request.HorseListRequest request,
        CancellationToken cancellationToken = default);

    Task<Response.HorseCreatedResponse> CreateHorseAsync(
        Guid accountId,
        Request.CreateHorseRequest request,
        CancellationToken cancellationToken = default);

    Task<Response.HorseDetailResponse> GetHorseAsync(
        Guid accountId,
        Guid horseId,
        bool canViewAll,
        CancellationToken cancellationToken = default);

    Task UpdateHorseAsync(
        Guid accountId,
        Guid horseId,
        Request.CreateHorseRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteHorseAsync(
        Guid accountId,
        Guid horseId,
        CancellationToken cancellationToken = default);
}
