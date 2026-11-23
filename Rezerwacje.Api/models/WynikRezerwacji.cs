namespace Rezerwacje.Api.models;

public record WynikRezerwacji(Rezerwacja? Rez, string? Blad = null, int Kod = 200);