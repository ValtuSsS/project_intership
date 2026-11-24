
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Rezerwacje.Api.Filtry;
using Rezerwacje.Api.Models;
using Rezerwacje.Api.Services;

namespace Rezerwacje.Api.Controllers;

[ApiController]
[Route("rezerwacje")]
[WymaganyKlientId]
public class RezerwacjeController : ControllerBase
{
    private readonly RezerwacjeService _rezerwacjeService;
    private readonly WysylkaRezerwacji _wysylkaRezerwacji;
    private readonly ILogger<RezerwacjeController> _log;

    public RezerwacjeController(
        RezerwacjeService rezerwacjeService,
        WysylkaRezerwacji wysylkaRezerwacji,
        ILogger<RezerwacjeController> log)
    {
        _rezerwacjeService = rezerwacjeService;
        _wysylkaRezerwacji = wysylkaRezerwacji;
        _log = log;
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

        try
        {
            await _wysylkaRezerwacji.WyslijRezerwacjeAsync(wynikRezerwacji.Rezerwacja);
        }
        catch (Exception blad)
        {
            _log.LogError(
                blad,
                "[{Pid}] Nie udało się wysłać rezerwacji {RezerwacjaId} do RabbitMQ",
                Environment.ProcessId,
                wynikRezerwacji.Rezerwacja.Id);
        }

        var adresRezerwacji = $"/rezerwacje/{Uri.EscapeDataString(wynikRezerwacji.Rezerwacja.Id)}";

        return Created(adresRezerwacji, wynikRezerwacji.Rezerwacja);
    }

    [HttpGet("moje")]
    public async Task<IActionResult> PobierzMojeRezerwacje(
        [FromHeader(Name = "X-Klient-Id")] string? klientId)
    {
        var (czyRezerwacjePobrane, rezerwacjeKlienta) =
            await _rezerwacjeService.PobierzRezerwacjeKlientaAsync(klientId!);

        if (!czyRezerwacjePobrane)
        {
            return Problem(detail: "Spróbuj ponownie", statusCode: 503);
        }

        return Ok(rezerwacjeKlienta);
    }
}
