using MongoDB.Driver;

using Rezerwacje.Api.Models;
using Rezerwacje.Api.Services;
using StackExchange.Redis;
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

builder.Services.AddControllers(opcje =>
{
    opcje.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
});

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

app.MapControllers();

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
            var liniaDoPominiecia = linia == "+OK" || linia.Contains("\"PING\"") || linia.Contains("\"INFO\"");
            if (liniaDoPominiecia)
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

app.Run();