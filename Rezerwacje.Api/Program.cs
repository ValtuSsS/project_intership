using MongoDB.Driver;

using Rezerwacje.Api.models;
using Rezerwacje.Api.Services;
using StackExchange.Redis;
using Microsoft.AspNetCore.Mvc;
using System.Net.Sockets;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BazaUstawienia>(builder.Configuration.GetSection("Baza"));

builder.Services.Configure<Cache>(builder.Configuration.GetSection("Cache"));
var mongoConnection = builder.Configuration["Baza:ConnectionString"];
var redisConnection = builder.Configuration["Redis:ConnectionString"];
Console.WriteLine($"Mongo: {mongoConnection}");
Console.WriteLine($"Redis: {redisConnection}");

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

await app.Services.GetRequiredService<LokaleService>().DodajStartoweAsync();
await app.Services.GetRequiredService<LokaleService>().DodajLokaleAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors();


app.Use(async (kont, dalej) =>
{
    kont.Response.Headers["X-Pid"] = Environment.ProcessId.ToString();
    await dalej();
});

app.MapGet("/", () => "dziala");

var grupa = app.MapGroup("/").AddEndpointFilter(async (kont, dalej) =>
{
    var klId = kont.HttpContext.Request.Headers["X-Klient-Id"].ToString();
    if (string.IsNullOrWhiteSpace(klId))
    {
        return Results.Problem("Brak klientId", statusCode: 400);
    }

    return await dalej(kont);
});

grupa.MapGet("/lokale", async (string? typ, string? fraza, LokaleService lokSrv) =>
    await lokSrv.SzukajAsync(typ, fraza));

grupa.MapGet("/lokale/{id}", async (string id, LokaleService lokSrv) =>
{
    var (ok, lok) = await lokSrv.PobierzCacheAsync(id);
    if (!ok)
    {
        return Results.Problem("Spróbuj ponownie", statusCode: 503);
    }

    return lok is null ? Results.NotFound() : Results.Ok(lok);
});

grupa.MapPost("/lokale", async (Lokal lok, LokaleService lokSrv) =>
    await lokSrv.DodajAsync(lok)
        ? Results.Created($"/lokale/{lok.Id}", lok)
        : Results.Problem("Lokal o takim id już istnieje", statusCode: 409));

grupa.MapPut("/lokale/{id}", async (string id, Lokal lok, LokaleService lokSrv) =>
    await lokSrv.EdytujAsync(id, lok)
        ? Results.Ok(lok)
        : Results.NotFound());

grupa.MapGet("/lokale/{lokId}/pracownicy/{prId}/wolne", async (
    string lokId,
    string prId,
    string? data,
    LokaleService lokSrv,
    RezerwacjeService rezSrv) =>
{
    if (!Format.Data(data))
    {
        return Results.Problem("Data w formacie yyyy-MM-dd", statusCode: 400);
    }

    var (okL, lok) = await lokSrv.PobierzCacheAsync(lokId);
    if (!okL)
    {
        return Results.Problem("Spróbuj ponownie", statusCode: 503);
    }

    if (lok is null || !lok.Pracownicy.Any(x => x.Id == prId))
    {
        return Results.NotFound();
    }

    var (okW, wolne) = await rezSrv.WolneAsync(lok, prId, data!);
    return okW
        ? Results.Ok(wolne)
        : Results.Problem("Spróbuj ponownie", statusCode: 503);
});

grupa.MapPost("/rezerwacje", async (
    [FromHeader(Name = "X-Klient-Id")] string? klId,
    NowaRezerwacja nowa,
    RezerwacjeService rezSrv) =>
{
    var wynik = await rezSrv.RezerwujAsync(klId!, nowa);
    return wynik.Rez is null
        ? Results.Problem(wynik.Blad, statusCode: wynik.Kod)
        : Results.Created($"/rezerwacje/{Uri.EscapeDataString(wynik.Rez.Id)}", wynik.Rez);
});

grupa.MapGet("/rezerwacje/moje", async (
    [FromHeader(Name = "X-Klient-Id")] string? klId,
    RezerwacjeService rezSrv) =>
{
    var (ok, moje) = await rezSrv.MojeAsync(klId!);
    return ok
        ? Results.Ok(moje)
        : Results.Problem("Spróbuj ponownie", statusCode: 503);
});

app.MapGet("/test/blokada", async (BlokadaRedis blok) =>
{
    var token = await blok.BlokadaAsync("lock:test");
    if (token is null)
    {
        return Results.Problem("Blokada zajęta", statusCode: 503);
    }

    await blok.ZwolnienieAsync("lock:test", token);
    return Results.Ok(token);
});


app.MapDelete("/test/cache", async (string klucz, CacheRedis cache) =>
{
    await cache.UsunAsync(klucz);
    return Results.Ok(klucz);
});


app.MapGet("/test/monitor", async (HttpContext kont, IConfiguration konf) =>
{
    kont.Response.ContentType = "text/event-stream";
    var adr = konf["Redis:ConnectionString"]!.Split(',')[0].Split(':');
    try
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(adr[0], int.Parse(adr[1]), kont.RequestAborted);
        using var str = tcp.GetStream();
        await str.WriteAsync("MONITOR\r\n"u8.ToArray(), kont.RequestAborted);
        using var czyt = new StreamReader(str);
        while (await czyt.ReadLineAsync(kont.RequestAborted) is { } lin)
        {
            if (lin == "+OK" || lin.Contains("\"PING\"") || lin.Contains("\"INFO\""))
            {
                continue;
            }

            await kont.Response.WriteAsync($"data: {lin.TrimStart('+')}\n\n", kont.RequestAborted);
            await kont.Response.Body.FlushAsync(kont.RequestAborted);
        }
    }
    catch (OperationCanceledException)
    {
    }
});

/*
app.MapGet("/test/cache", async (CacheRedis cache) =>
{
    var lista = await cache.PobierzAsync(
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