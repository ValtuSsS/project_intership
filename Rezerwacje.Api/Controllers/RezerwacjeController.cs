using Microsoft.AspNetCore.Mvc;
using Rezerwacje.Api.Filtry;
using Rezerwacje.Api.models;
using Rezerwacje.Api.Services;

namespace Rezerwacje.Api.Controllers;

[ApiController]
[Route("rezerwacje")]
[WymaganyKlientId]
public class RezerwacjeController : ControllerBase
{
    private readonly RezerwacjeService _rezerwacjeService;

    public RezerwacjeController(RezerwacjeService rezerwacjeService)
    {
        _rezerwacjeService = rezerwacjeService;
    }

    [HttpPost]
    public async Task<IActionResult> Zarezerwuj(
        [FromHeader(Name = "X-Klient-Id")] string? klientId,
        [FromBody] NowaRezerwacja nowaRezerwacja)
    {
        var wynikRezerwacji = await _rezerwacjeService.RezerwujAsync(klientId!, nowaRezerwacja);
        if (wynikRezerwacji.Rezerwacja is null)
        {
            return Problem(detail: wynikRezerwacji.Blad, statusCode: wynikRezerwacji.Kod);
        }

        var adresRezerwacji = $"/rezerwacje/{Uri.EscapeDataString(wynikRezerwacji.Rezerwacja.Id)}";
        return Created(adresRezerwacji, wynikRezerwacji.Rezerwacja);
    }

    [HttpGet("moje")]
    public async Task<IActionResult> PobierzMojeRezerwacje([FromHeader(Name = "X-Klient-Id")] string? klientId)
    {
        var (czyRezerwacjePobrane, rezerwacjeKlienta) = await _rezerwacjeService.PobierzRezerwacjeKlientaAsync(klientId!);
        if (!czyRezerwacjePobrane)
        {
            return Problem(detail: "Spróbuj ponownie", statusCode: 503);
        }

        return Ok(rezerwacjeKlienta);
    }
}
