using System.Globalization;
using Rift.Core.Localization;

namespace Rift.Core;

public static class SizeFormatter
{
    private static string[] Units => [Strings.SizeB, Strings.SizeKB, Strings.SizeMB, Strings.SizeGB, Strings.SizeTB];

    public static string Format(long? bytes)
    {
        if (bytes is null)
            return "";

        var units = Units;
        double value = bytes.Value;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = unit == 0 ? "0" : value < 10 ? "0.#" : "0";
        return value.ToString(format, CultureInfo.CurrentCulture) + " " + units[unit];
    }
}
