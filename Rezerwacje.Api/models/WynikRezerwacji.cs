namespace Rezerwacje.Api.models;

public record WynikRezerwacji(Rezerwacja? Rezerwacja, string? Blad = null, int Kod = 200);