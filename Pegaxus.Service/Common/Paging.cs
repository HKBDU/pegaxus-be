namespace Pegaxus.Service.Common;

public static class Paging
{
    public static int TotalPages(int totalItems, int pageSize) =>
        (int)Math.Ceiling(totalItems / (double)pageSize);
}
