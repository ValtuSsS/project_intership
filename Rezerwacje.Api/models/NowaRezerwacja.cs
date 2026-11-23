namespace Rezerwacje.Api.models;

public record NowaRezerwacja(
    string LokalId,
    string PracownikId,
    /*    string Usluga, */
    string Data,
    string Godzina);
