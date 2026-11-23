using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Rezerwacje.Api.Models;
using System.Text.RegularExpressions;
using MongoDB.Bson;

namespace Rezerwacje.Api.Services;

public class LokaleService
{
    private readonly IMongoCollection<Lokal> _lokale;
    private readonly CacheRedis _cache;
    private readonly UstawieniaCache _ustawienia;
    private readonly ILogger<LokaleService> _log;

    public LokaleService(
        IMongoClient mongo,
        IOptions<UstawieniaBazy> ustawieniaBazy,
        IOptions<UstawieniaCache> ustawienia,
        CacheRedis cache,
        ILogger<LokaleService> log)
    {
        _lokale = mongo.GetDatabase(ustawieniaBazy.Value.Nazwa).GetCollection<Lokal>("lokale");
        _cache = cache;
        _ustawienia = ustawienia.Value;
        _log = log;
    }

    public async Task<List<Lokal>> PobierzWszystkieAsync()
    {
        return await _lokale.Find(lokal => true).ToListAsync();
    }

    public async Task<List<Lokal>> SzukajAsync(string? typ, string? fraza)
    {
        var tworzenieFiltru = Builders<Lokal>.Filter;
        var filtr = tworzenieFiltru.Empty;

        if (!string.IsNullOrWhiteSpace(typ))
        {
            filtr &= tworzenieFiltru.Eq(lokal => lokal.Typ, typ);
        }

        if (!string.IsNullOrWhiteSpace(fraza))
        {
            filtr &= tworzenieFiltru.Regex(lokal => lokal.Nazwa, new BsonRegularExpression(Regex.Escape(fraza), "i"));
        }

        return await _lokale.Find(filtr).ToListAsync();
    }

    public async Task<Lokal?> PobierzLokalZBazyAsync(string lokalId)
    {
        return await _lokale.Find(lokal => lokal.Id == lokalId).FirstOrDefaultAsync();
    }

    public async Task<(bool CzyPobrano, Lokal? Dane)> PobierzLokalAsync(string lokalId)
    {
        var kluczLokalu = $"lokal:{lokalId}";
        var kluczBlokady = $"lock:lokal:{lokalId}";
        var czasZyciaCache = TimeSpan.FromSeconds(_ustawienia.CzasCacheLokaluS);

        return await _cache.PobierzZCacheLubBazyAsync<Lokal?>(
            kluczLokalu,
            kluczBlokady,
            czasZyciaCache,
            () => PobierzLokalZBazyAsync(lokalId));
    }

    public async Task<bool> DodajAsync(Lokal lokal)
    {
        try
        {
            await _lokale.InsertOneAsync(lokal);
            return true;
        }
        catch (MongoWriteException wyjatek) when (wyjatek.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<bool> EdytujAsync(string lokalId, Lokal lokal)
    {
        lokal.Id = lokalId;
        var wynikZapisu = await _lokale.ReplaceOneAsync(zapisany => zapisany.Id == lokalId, lokal);
        if (wynikZapisu.MatchedCount == 0)
        {
            return false;
        }

        await _cache.UsunZCacheAsync($"lokal:{lokalId}");
        _log.LogInformation("[{Pid}] lokal:{Id}: edytowany, usunięty z redis", Environment.ProcessId, lokalId);
        return true;
    }

    public async Task DodajSalonStartowyAsync()
    {
        try
        {
            await _lokale.InsertOneAsync(DaneStartowe.SalonStartowy());
            _log.LogInformation("Dodano dane startowe");
        }
        catch (MongoWriteException wyjatek) when (wyjatek.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            _log.LogInformation("Dane startowe są w bazie");
        }
    }

    public async Task DodajLokaleStartoweAsync()
    {
        var liczbaDodanych = 0;
        foreach (var lokal in DaneStartowe.LokaleStartowe())
        {
            if (await DodajAsync(lokal))
            {
                liczbaDodanych++;
            }
        }

        _log.LogInformation("[{Pid}] Lokale startowe: dodano {LiczbaDodanych}", Environment.ProcessId, liczbaDodanych);
    }
}
