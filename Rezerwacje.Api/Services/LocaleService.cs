using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Rezerwacje.Api.models;
using System.Text.RegularExpressions;
using MongoDB.Bson;

namespace Rezerwacje.Api.Services;

public class LokaleService
{
    private readonly IMongoCollection<Lokal> _lok;
    private readonly CacheRedis _cache;
    private readonly Cache _ust;
    private readonly ILogger<LokaleService> _log;

    public LokaleService(
        IMongoClient mongo,
        IOptions<BazaUstawienia> baza,
        IOptions<Cache> ust,
        CacheRedis cache,
        ILogger<LokaleService> log)
    {
        _lok = mongo.GetDatabase(baza.Value.Nazwa).GetCollection<Lokal>("lokale");
        _cache = cache;
        _ust = ust.Value;
        _log = log;
    }

    public async Task<List<Lokal>> WszystkieAsync() =>
        await _lok.Find(_ => true).ToListAsync();

    public async Task<List<Lokal>> SzukajAsync(string? typ, string? fraza)
    {
        var f = Builders<Lokal>.Filter;
        var filtr = f.Empty;

        if (!string.IsNullOrWhiteSpace(typ))
        {
            filtr &= f.Eq(x => x.Typ, typ);
        }

        if (!string.IsNullOrWhiteSpace(fraza))
        {
            filtr &= f.Regex(x => x.Nazwa, new BsonRegularExpression(Regex.Escape(fraza), "i"));
        }

        return await _lok.Find(filtr).ToListAsync();
    }

    public async Task<Lokal?> PobierzAsync(string id) =>
        await _lok.Find(x => x.Id == id).FirstOrDefaultAsync();

    public async Task<(bool Ok, Lokal? Wart)> PobierzCacheAsync(string id) =>
        await _cache.PobierzAsync<Lokal?>(
            $"lokal:{id}",
            $"lock:lokal:{id}",
            TimeSpan.FromSeconds(_ust.LokalTtlS),
            () => PobierzAsync(id));

    public async Task<bool> DodajAsync(Lokal lok)
    {
        try
        {
            await _lok.InsertOneAsync(lok);
            return true;
        }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<bool> EdytujAsync(string id, Lokal lok)
    {
        lok.Id = id;
        var wynik = await _lok.ReplaceOneAsync(x => x.Id == id, lok);
        if (wynik.MatchedCount == 0)
        {
            return false;
        }

        await _cache.UsunAsync($"lokal:{id}");
        _log.LogInformation("[{Pid}] lokal:{Id}: edytowany, usunięty z redis", Environment.ProcessId, id);
        return true;
    }

    public async Task DodajStartoweAsync()
    {
        try
        {
            await _lok.InsertOneAsync(Dane.Salon());
            _log.LogInformation("Dodano dane startowe");
        }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            _log.LogInformation("Dane startowe są w bazie");
        }
    }
        public async Task DodajLokaleAsync()
    {
        var nowe = 0;
        foreach (var lok in Dane.Lokale())
        {
            if (await DodajAsync(lok))
            {
                nowe++;
            }
        }

        _log.LogInformation("[{Pid}] Lokale startowe: dodano {Nowe}", Environment.ProcessId, nowe);
    }
}