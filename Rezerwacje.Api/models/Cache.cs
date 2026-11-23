using System;

namespace Rezerwacje.Api.models;

public class Cache
{
    public int KluczTtlMls { get; set; }

    public int PrzerwaMs { get; set; }

    public int Proby { get; set; }

    public int HarmonS { get; set; }

    public int RezerwTtlS { get; set; }
    public int ProbyOdczyt { get; set; }

    public int OpoznienieMs { get; set; }
    public int LokalTtlS { get; set; }
}
