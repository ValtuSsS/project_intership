using System;

namespace Rezerwacje.Api.models;

public class UstawieniaCache
{
    public int CzasBlokadyMs { get; set; }

    public int PrzerwaMiedzyProbamiMs { get; set; }

    public int ProbyBlokadyRezerwacji { get; set; }

    public int CzasCacheWolnychGodzinS { get; set; }

    public int CzasCacheRezerwacjiKlientaS { get; set; }
    public int ProbyOdczytuCache { get; set; }

    public int TestoweOpoznienieMs { get; set; }
    public int CzasCacheLokaluS { get; set; }
}
