namespace Rezerwacje.Api.Models;

public record WynikRezerwacji(Rezerwacja? Rezerwacja, string? Blad = null, int Kod = 200);