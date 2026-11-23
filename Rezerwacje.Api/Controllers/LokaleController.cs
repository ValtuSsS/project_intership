using Microsoft.AspNetCore.Mvc;
using Rezerwacje.Api.Filtry;
using Rezerwacje.Api.models;
using Rezerwacje.Api.Services;

namespace Rezerwacje.Api.Controllers;

[ApiController]
[Route("lokale")]
[WymaganyKlientId]
public class LokaleController : ControllerBase
{
    private readonly LokaleService _lokaleService;
    private readonly RezerwacjeService _rezerwacjeService;

    public LokaleController(LokaleService lokaleService, RezerwacjeService rezerwacjeService)
    {
        _lokaleService = lokaleService;
        _rezerwacjeService = rezerwacjeService;
    }

    [HttpGet]
    public async Task<IActionResult> SzukajLokali([FromQuery] string? typ, [FromQuery] string? fraza)
    {
        var znalezioneLokale = await _lokaleService.SzukajAsync(typ, fraza);
        return Ok(znalezioneLokale);
    }

    [HttpGet("{lokalId}")]
    public async Task<IActionResult> PobierzLokal(string lokalId)
    {
        var (czyLokalPobrany, lokal) = await _lokaleService.PobierzLokalAsync(lokalId);
        if (!czyLokalPobrany)
        {
            return Problem(detail: "Spróbuj ponownie", statusCode: 503);
        }

        if (lokal is null)
        {
            return NotFound();
        }

        return Ok(lokal);
    }

    [HttpPost]
    public async Task<IActionResult> DodajLokal([FromBody] Lokal lokal)
    {
        var czyDodany = await _lokaleService.DodajAsync(lokal);
        if (!czyDodany)
        {
            return Problem(detail: "Lokal o takim id już istnieje", statusCode: 409);
        }

        return Created($"/lokale/{lokal.Id}", lokal);
    }

    [HttpPut("{lokalId}")]
    public async Task<IActionResult> EdytujLokal(string lokalId, [FromBody] Lokal lokal)
    {
        var czyEdytowany = await _lokaleService.EdytujAsync(lokalId, lokal);
        if (!czyEdytowany)
        {
            return NotFound();
        }

        return Ok(lokal);
    }

    [HttpGet("{lokalId}/pracownicy/{pracownikId}/wolne")]
    public async Task<IActionResult> PobierzWolneGodziny(string lokalId, string pracownikId, [FromQuery] string? data)
    {
        if (!Format.CzyPoprawnaData(data))
        {
            return Problem(detail: "Data w formacie yyyy-MM-dd", statusCode: 400);
        }

        var (czyLokalPobrany, lokal) = await _lokaleService.PobierzLokalAsync(lokalId);
        if (!czyLokalPobrany)
        {
            return Problem(detail: "Spróbuj ponownie", statusCode: 503);
        }

        if (lokal is null || !lokal.Pracownicy.Any(pracownik => pracownik.Id == pracownikId))
        {
            return NotFound();
        }

        var (czyWolneGodzinyPobrane, wolneGodziny) = await _rezerwacjeService.PobierzWolneGodzinyAsync(lokal, pracownikId, data!);
        if (!czyWolneGodzinyPobrane)
        {
            return Problem(detail: "Spróbuj ponownie", statusCode: 503);
        }

        return Ok(wolneGodziny);
    }
}
