namespace Pegaxus.Service.HorseService;

public static class Response
{
    public sealed class HorseListResponse
    {
        public required IReadOnlyList<HorseItemResponse> Items { get; init; }
        public int PageIndex { get; init; }
        public int PageSize { get; init; }
        public int TotalItems { get; init; }
        public int TotalPages { get; init; }
    }

    public sealed class HorseItemResponse
    {
        public Guid HorseId { get; init; }
        public required string HorseName { get; init; }
        public string? Sex { get; init; }
        public string? Breed { get; init; }
        public string? MicrochipNumber { get; init; }
    }

    public sealed class HorseCreatedResponse
    {
        public Guid HorseId { get; init; }
        public required string HorseName { get; init; }
    }

    public sealed class HorseDetailResponse
    {
        public Guid HorseId { get; init; }
        public required string HorseName { get; init; }
        public string? Sex { get; init; }
        public string? Breed { get; init; }
        public DateOnly? DateOfBirth { get; init; }
        public string? Color { get; init; }
        public string? CountryOfOrigin { get; init; }
        public string? PassportNumber { get; init; }
        public string? MicrochipNumber { get; init; }
        public string? IdentificationNotes { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }
}
