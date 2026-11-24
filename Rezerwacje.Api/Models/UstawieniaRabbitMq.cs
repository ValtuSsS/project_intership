
using System;

namespace Rezerwacje.Api.Models;

public class UstawieniaRabbitMq
{
    public string Host { get; set; } = null!;

    public string NazwaExchange { get; set; } = null!;
}
