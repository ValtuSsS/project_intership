using System;

namespace Rezerwacje.Api.Models;

public class UstawieniaBazy
{
    public string ConnectionString {get; set; } = null!;
    public string Nazwa {get; set; } = null!;
}
