using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Common;

public static class Paging
{
    public static int TotalPages(int totalItems, int pageSize) =>
        (int)Math.Ceiling(totalItems / (double)pageSize);

    /// <summary>
    /// Rows to skip for a 1-based page. The multiplication is done in <c>long</c> because an int overflow
    /// would turn a huge pageIndex into a negative OFFSET, which PostgreSQL rejects with a 500.
    /// </summary>
    public static int Offset(int pageIndex, int pageSize)
    {
        var offset = ((long)pageIndex - 1) * pageSize;
        if (pageIndex < 1 || pageSize < 1 || offset > int.MaxValue)
            throw new BadRequestException("The requested page is out of range.", "VALIDATION_ERROR");
        return (int)offset;
    }
}
