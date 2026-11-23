namespace Rezerwacje.Api.Models;

public record NowaRezerwacja(
    string LokalId,
    string PracownikId,
    /*    string Usluga, */
    string Data,
    string Godzina);
