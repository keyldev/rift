using System.Globalization;

namespace Rift.Core;

public static class SizeFormatter
{
    private static readonly string[] Units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];

    public static string Format(long? bytes)
    {
        if (bytes is null)
            return "";

        double value = bytes.Value;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = unit == 0 ? "0" : value < 10 ? "0.#" : "0";
        return value.ToString(format, CultureInfo.CurrentCulture) + " " + Units[unit];
    }
}
