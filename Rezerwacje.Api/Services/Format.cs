using System;

using System.Globalization;

namespace Rezerwacje.Api.Services;

public static class Format
{
    public static bool CzyPoprawnaData(string? tekst) =>
        DateOnly.TryParseExact(tekst, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static bool CzyPelnaGodzina(string? tekst) =>
        TimeOnly.TryParseExact(tekst, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var godzina)
        && godzina.Minute == 0;
}
