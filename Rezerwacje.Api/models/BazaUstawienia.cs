using System;

namespace Rezerwacje.Api.models;

public class BazaUstawienia
{
    public string ConnectionString {get; set; } = null!;
    public string Nazwa {get; set; } = null!;
}
