using MongoDB.Driver;

using Rezerwacje.Api.models;
using Rezerwacje.Api.Services;
using StackExchange.Redis;
using Microsoft.AspNetCore.Mvc;
using System.Net.Sockets;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<UstawieniaBazy>(builder.Configuration.GetSection("Baza"));

builder.Services.Configure<UstawieniaCache>(builder.Configuration.GetSection("Cache"));
var adresMongo = builder.Configuration["Baza:ConnectionString"];
var adresRedis = builder.Configuration["Redis:ConnectionString"];
Console.WriteLine($"Mongo: {adresMongo}");
Console.WriteLine($"Redis: {adresRedis}");

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    return ConnectionMultiplexer.Connect(adresRedis!);
});

builder.Services.AddSingleton<IMongoClient>(_ =>
{
    return new MongoClient(adresMongo);
});

builder.Services.AddSingleton<BlokadaRedis>();
builder.Services.AddSingleton<CacheRedis>();
builder.Services.AddSingleton<LokaleService>();

builder.Services.AddSingleton<RezerwacjeService>();

builder.Services.AddOpenApi();

builder.Services.AddCors(opcje =>
{
    opcje.AddDefaultPolicy(zasady =>
    {
        zasady.WithOrigins("http://localhost:5126", "http://localhost:5127")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("X-Pid");
    });
});

var app = builder.Build();

await app.Services.GetRequiredService<LokaleService>().DodajSalonStartowyAsync();
await app.Services.GetRequiredService<LokaleService>().DodajLokaleStartoweAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors();


app.Use(async (kontekst, nastepny) =>
{
    kontekst.Response.Headers["X-Pid"] = Environment.ProcessId.ToString();
    await nastepny();
});

app.MapGet("/", () =>
{
    return "dziala";
});

var grupa = app.MapGroup("/").AddEndpointFilter(async (kontekst, nastepny) =>
{
    var klientId = kontekst.HttpContext.Request.Headers["X-Klient-Id"].ToString();
    if (string.IsNullOrWhiteSpace(klientId))
    {
        return Results.Problem("Brak klientId", statusCode: 400);
    }

    return await nastepny(kontekst);
});

grupa.MapGet("/lokale", async (string? typ, string? fraza, LokaleService lokaleService) =>
{
    var znalezioneLokale = await lokaleService.SzukajAsync(typ, fraza);
    return Results.Ok(znalezioneLokale);
});

grupa.MapGet("/lokale/{lokalId}", async (string lokalId, LokaleService lokaleService) =>
{
    var (czyLokalPobrany, lokal) = await lokaleService.PobierzLokalAsync(lokalId);
    if (!czyLokalPobrany)
    {
        return Results.Problem("Spróbuj ponownie", statusCode: 503);
    }

    if (lokal is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(lokal);
});

grupa.MapPost("/lokale", async (Lokal lokal, LokaleService lokaleService) =>
{
    var czyDodany = await lokaleService.DodajAsync(lokal);
    if (!czyDodany)
    {
        return Results.Problem("Lokal o takim id już istnieje", statusCode: 409);
    }

    return Results.Created($"/lokale/{lokal.Id}", lokal);
});

grupa.MapPut("/lokale/{lokalId}", async (string lokalId, Lokal lokal, LokaleService lokaleService) =>
{
    var czyEdytowany = await lokaleService.EdytujAsync(lokalId, lokal);
    if (!czyEdytowany)
    {
        return Results.NotFound();
    }

    return Results.Ok(lokal);
});

grupa.MapGet("/lokale/{lokalId}/pracownicy/{pracownikId}/wolne", async (
    string lokalId,
    string pracownikId,
    string? data,
    LokaleService lokaleService,
    RezerwacjeService rezerwacjeService) =>
{
    if (!Format.CzyPoprawnaData(data))
    {
        return Results.Problem("Data w formacie yyyy-MM-dd", statusCode: 400);
    }

    var (czyLokalPobrany, lokal) = await lokaleService.PobierzLokalAsync(lokalId);
    if (!czyLokalPobrany)
    {
        return Results.Problem("Spróbuj ponownie", statusCode: 503);
    }

    if (lokal is null || !lokal.Pracownicy.Any(pracownik => pracownik.Id == pracownikId))
    {
        return Results.NotFound();
    }

    var (czyWolneGodzinyPobrane, wolneGodziny) = await rezerwacjeService.PobierzWolneGodzinyAsync(lokal, pracownikId, data!);
    if (!czyWolneGodzinyPobrane)
    {
        return Results.Problem("Spróbuj ponownie", statusCode: 503);
    }

    return Results.Ok(wolneGodziny);
});

grupa.MapPost("/rezerwacje", async (
    [FromHeader(Name = "X-Klient-Id")] string? klientId,
    NowaRezerwacja nowaRezerwacja,
    RezerwacjeService rezerwacjeService) =>
{
    var wynikRezerwacji = await rezerwacjeService.RezerwujAsync(klientId!, nowaRezerwacja);
    if (wynikRezerwacji.Rezerwacja is null)
    {
        return Results.Problem(wynikRezerwacji.Blad, statusCode: wynikRezerwacji.Kod);
    }

    var adresRezerwacji = $"/rezerwacje/{Uri.EscapeDataString(wynikRezerwacji.Rezerwacja.Id)}";
    return Results.Created(adresRezerwacji, wynikRezerwacji.Rezerwacja);
});

grupa.MapGet("/rezerwacje/moje", async (
    [FromHeader(Name = "X-Klient-Id")] string? klientId,
    RezerwacjeService rezerwacjeService) =>
{
    var (czyRezerwacjePobrane, rezerwacjeKlienta) = await rezerwacjeService.PobierzRezerwacjeKlientaAsync(klientId!);
    if (!czyRezerwacjePobrane)
    {
        return Results.Problem("Spróbuj ponownie", statusCode: 503);
    }

    return Results.Ok(rezerwacjeKlienta);
});

app.MapGet("/test/blokada", async (BlokadaRedis blokada) =>
{
    var token = await blokada.SprobujZalozycBlokadeAsync("lock:test");
    if (token is null)
    {
        return Results.Problem("Blokada zajęta", statusCode: 503);
    }

    await blokada.ZwolnijBlokadeAsync("lock:test", token);
    return Results.Ok(token);
});


app.MapDelete("/test/cache", async (string klucz, CacheRedis cache) =>
{
    await cache.UsunZCacheAsync(klucz);
    return Results.Ok(klucz);
});


app.MapGet("/test/monitor", async (HttpContext kontekst, IConfiguration konfiguracja) =>
{
    kontekst.Response.ContentType = "text/event-stream";
    var adresRedisa = konfiguracja["Redis:ConnectionString"]!.Split(',')[0].Split(':');
    try
    {
        using var polaczenie = new TcpClient();
        await polaczenie.ConnectAsync(adresRedisa[0], int.Parse(adresRedisa[1]), kontekst.RequestAborted);
        using var strumien = polaczenie.GetStream();
        await strumien.WriteAsync("MONITOR\r\n"u8.ToArray(), kontekst.RequestAborted);
        using var czytnik = new StreamReader(strumien);
        while (await czytnik.ReadLineAsync(kontekst.RequestAborted) is { } linia)
        {
            if (linia == "+OK" || linia.Contains("\"PING\"") || linia.Contains("\"INFO\""))
            {
                continue;
            }

            await kontekst.Response.WriteAsync($"data: {linia.TrimStart('+')}\n\n", kontekst.RequestAborted);
            await kontekst.Response.Body.FlushAsync(kontekst.RequestAborted);
        }
    }
    catch (OperationCanceledException)
    {
    }
});

/*
app.MapGet("/test/cache", async (CacheRedis cache) =>
{
    var lista = await cache.PobierzZCacheLubBazyAsync(
        "test:lista",
        "lock:test",
        TimeSpan.FromSeconds(60),
        async () =>
        {
            await Task.Delay(800);
            return new List<string> { "09:00", "10:00" };
        });

    return lista is null
        ? Results.Problem("Spróbuj ponownie", statusCode: 503)
        : Results.Ok(lista);
});

*/
app.Run();