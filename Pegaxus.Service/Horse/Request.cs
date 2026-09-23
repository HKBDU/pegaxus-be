namespace Pegaxus.Service.HorseService;

public static class Request
{
    public sealed class HorseListRequest
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public string? Search { get; set; }
    }

    public sealed class CreateHorseRequest
    {
        public string HorseName { get; set; } = string.Empty;
        public string? Sex { get; set; }
        public string? Breed { get; set; }
        public DateOnly? DateOfBirth { get; set; }
        public string? CountryOfOrigin { get; set; }
        public string? MicrochipNumber { get; set; }
        public string? PassportNumber { get; set; }
    }
}
