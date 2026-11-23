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
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"]!));

builder.Services.AddSingleton<IMongoClient>(_ =>
    new MongoClient(builder.Configuration["Baza:ConnectionString"]));

builder.Services.AddSingleton<BlokadaRedis>();
builder.Services.AddSingleton<CacheRedis>();
builder.Services.AddSingleton<LokaleService>();

builder.Services.AddSingleton<RezerwacjeService>();

builder.Services.AddOpenApi();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins("http://localhost:5126", "http://localhost:5127")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders("X-Pid")));

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

app.MapGet("/", () => "dziala");

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
    await lokaleService.SzukajAsync(typ, fraza));

grupa.MapGet("/lokale/{lokalId}", async (string lokalId, LokaleService lokaleService) =>
{
    var (czyLokalPobrany, lokal) = await lokaleService.PobierzLokalAsync(lokalId);
    if (!czyLokalPobrany)
    {
        return Results.Problem("Spróbuj ponownie", statusCode: 503);
    }

    return lokal is null ? Results.NotFound() : Results.Ok(lokal);
});

grupa.MapPost("/lokale", async (Lokal lokal, LokaleService lokaleService) =>
    await lokaleService.DodajAsync(lokal)
        ? Results.Created($"/lokale/{lokal.Id}", lokal)
        : Results.Problem("Lokal o takim id już istnieje", statusCode: 409));

grupa.MapPut("/lokale/{lokalId}", async (string lokalId, Lokal lokal, LokaleService lokaleService) =>
    await lokaleService.EdytujAsync(lokalId, lokal)
        ? Results.Ok(lokal)
        : Results.NotFound());

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
    return czyWolneGodzinyPobrane
        ? Results.Ok(wolneGodziny)
        : Results.Problem("Spróbuj ponownie", statusCode: 503);
});

grupa.MapPost("/rezerwacje", async (
    [FromHeader(Name = "X-Klient-Id")] string? klientId,
    NowaRezerwacja nowaRezerwacja,
    RezerwacjeService rezerwacjeService) =>
{
    var wynikRezerwacji = await rezerwacjeService.RezerwujAsync(klientId!, nowaRezerwacja);
    return wynikRezerwacji.Rezerwacja is null
        ? Results.Problem(wynikRezerwacji.Blad, statusCode: wynikRezerwacji.Kod)
        : Results.Created($"/rezerwacje/{Uri.EscapeDataString(wynikRezerwacji.Rezerwacja.Id)}", wynikRezerwacji.Rezerwacja);
});

grupa.MapGet("/rezerwacje/moje", async (
    [FromHeader(Name = "X-Klient-Id")] string? klientId,
    RezerwacjeService rezerwacjeService) =>
{
    var (czyRezerwacjePobrane, rezerwacjeKlienta) = await rezerwacjeService.PobierzRezerwacjeKlientaAsync(klientId!);
    return czyRezerwacjePobrane
        ? Results.Ok(rezerwacjeKlienta)
        : Results.Problem("Spróbuj ponownie", statusCode: 503);
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