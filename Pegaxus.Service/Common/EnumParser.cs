namespace Pegaxus.Service.Common;

public static class EnumParser
{
    /// <summary>
    /// Case-insensitive parse that only accepts a declared member name. <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/>
    /// also accepts numeric strings ("99") and comma lists, which produce values that do not exist in the enum.
    /// </summary>
    public static bool TryParseDefined<T>(string? value, out T result) where T : struct, Enum
    {
        result = default;
        var name = value?.Trim();
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var candidate in Enum.GetNames<T>())
        {
            if (!string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)) continue;
            result = Enum.Parse<T>(candidate);
            return true;
        }
        return false;
    }
}
