using System;

using System.Globalization;

namespace Rezerwacje.Api.Services;

public static class Format
{
    public static bool Data(string? tekst) =>
        DateOnly.TryParseExact(tekst, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static bool Godzina(string? tekst) =>
        TimeOnly.TryParseExact(tekst, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var godz)
        && godz.Minute == 0;
}
