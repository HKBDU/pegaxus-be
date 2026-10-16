using System.ComponentModel.DataAnnotations;

namespace Pegaxus.Service.HorseHealthService;

public static class Request
{
    public sealed class ListRequest
    {
        [DisplayFormat(ConvertEmptyStringToNull = false)]
        public string? LogType { get; set; }
        [DisplayFormat(ConvertEmptyStringToNull = false)]
        public string? HealthStatus { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; } = 20;
    }
}
