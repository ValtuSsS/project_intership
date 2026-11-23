using System;

using System.Globalization;

namespace Rezerwacje.Api.Services;

public static class Format
{
    public static bool CzyPoprawnaData(string? tekst)
    {
        return DateOnly.TryParseExact(tekst, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    public static bool CzyPelnaGodzina(string? tekst)
    {
        var czyPoprawnyFormat = TimeOnly.TryParseExact(
            tekst, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var godzina);

        return czyPoprawnyFormat && godzina.Minute == 0;
    }
}
