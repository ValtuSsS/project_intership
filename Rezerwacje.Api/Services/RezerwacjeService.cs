using System.Globalization;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Rezerwacje.Api.models;

namespace Rezerwacje.Api.Services;

public class RezerwacjeService
{
    private readonly IMongoCollection<Rezerwacja> _rezerwacje;
    private readonly LokaleService _lokaleService;
    private readonly CacheRedis _cache;
    private readonly BlokadaRedis _blokada;
    private readonly UstawieniaCache _ustawienia;
    private readonly ILogger<RezerwacjeService> _log;

    public RezerwacjeService(
        IMongoClient mongo,
        IOptions<UstawieniaBazy> ustawieniaBazy,
        IOptions<UstawieniaCache> ustawienia,
        LokaleService lokaleService,
        CacheRedis cache,
        BlokadaRedis blokada,
        ILogger<RezerwacjeService> log)
    {
        _rezerwacje = mongo.GetDatabase(ustawieniaBazy.Value.Nazwa).GetCollection<Rezerwacja>("rezerwacje");
        _lokaleService = lokaleService;
        _cache = cache;
        _blokada = blokada;
        _ustawienia = ustawienia.Value;
        _log = log;
    }

    public async Task<(bool CzyPobrano, List<string>? Dane)> PobierzWolneGodzinyAsync(Lokal lokal, string pracownikId, string data) =>
        await _cache.PobierzZCacheLubBazyAsync(
            $"wolne:{lokal.Id}:{pracownikId}:{data}",
            $"lock:{lokal.Id}:{pracownikId}:{data}",
            TimeSpan.FromSeconds(_ustawienia.CzasCacheWolnychGodzinS),
            () => PoliczWolneGodzinyZBazyAsync(lokal, pracownikId, data));

    public async Task<(bool CzyPobrano, List<Rezerwacja>? Dane)> PobierzRezerwacjeKlientaAsync(string klientId) =>
        await _cache.PobierzZCacheLubBazyAsync(
            $"rezerwacje:klient:{klientId}",
            $"rebuild:klient:{klientId}",
            TimeSpan.FromSeconds(_ustawienia.CzasCacheRezerwacjiKlientaS),
            () => PobierzRezerwacjeKlientaZBazyAsync(klientId));

    public async Task<WynikRezerwacji> RezerwujAsync(string klientId, NowaRezerwacja nowaRezerwacja)
    {
        if (!Format.CzyPoprawnaData(nowaRezerwacja.Data) || !Format.CzyPelnaGodzina(nowaRezerwacja.Godzina))
        {
            return new(null, "Data yyyy-MM-dd, godzina pełna HH:00", 400);
        }

        var kluczBlokady = $"lock:{nowaRezerwacja.LokalId}:{nowaRezerwacja.PracownikId}:{nowaRezerwacja.Data}";
        var token = await _blokada.ZalozBlokadePetlaAsync(kluczBlokady);
        if (token is null)
        {
            return new(null, "Spróbuj ponownie", 503);
        }

        try
        {
            var lokal = await _lokaleService.PobierzLokalZBazyAsync(nowaRezerwacja.LokalId);
            if (lokal is null || !lokal.Pracownicy.Any(pracownik => pracownik.Id == nowaRezerwacja.PracownikId))
            {
                return new(null, "Nie ma takiego lokalu lub pracownika", 404);
            }

            var wolneGodziny = await PoliczWolneGodzinyZBazyAsync(lokal, nowaRezerwacja.PracownikId, nowaRezerwacja.Data);
            _log.LogInformation("[{Pid}] {Lok} {Prac} {Data}: cosmos, wolnych godzin {Ile}",
                Environment.ProcessId, nowaRezerwacja.LokalId, nowaRezerwacja.PracownikId, nowaRezerwacja.Data, wolneGodziny.Count);
            if (!wolneGodziny.Contains(nowaRezerwacja.Godzina))
            {
                _log.LogInformation("[{Pid}] {Lok} {Prac} {Data} {Godz}: termin zajęty",
                    Environment.ProcessId, nowaRezerwacja.LokalId, nowaRezerwacja.PracownikId, nowaRezerwacja.Data, nowaRezerwacja.Godzina);
                return new(null, "Termin zajęty lub poza godzinami otwarcia", 409);
            }

            var rezerwacja = new Rezerwacja
            {
                Id = $"{nowaRezerwacja.LokalId}|{nowaRezerwacja.PracownikId}|{nowaRezerwacja.Data}|{nowaRezerwacja.Godzina}",
                KlientId = klientId,
                LokalId = nowaRezerwacja.LokalId,
                PracownikId = nowaRezerwacja.PracownikId,
                Data = nowaRezerwacja.Data,
                Godzina = nowaRezerwacja.Godzina,
                Utworzono = DateTime.UtcNow
            };

            try
            {
                await _rezerwacje.InsertOneAsync(rezerwacja);
            }
            catch (MongoWriteException wyjatek) when (wyjatek.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                _log.LogWarning("[{Pid}] {Id}: duplikat _id", Environment.ProcessId, rezerwacja.Id);
                return new(null, "Termin zajęty", 409);
            }

            _log.LogInformation("[{Pid}] {Id}: cosmos, zapisana dla {Klient}", Environment.ProcessId, rezerwacja.Id, klientId);

            var kluczWolnychGodzin = $"wolne:{nowaRezerwacja.LokalId}:{nowaRezerwacja.PracownikId}:{nowaRezerwacja.Data}";
            var kluczRezerwacjiKlienta = $"rezerwacje:klient:{klientId}";
            await _cache.UsunZCacheAsync(kluczWolnychGodzin, kluczRezerwacjiKlienta);
            _log.LogInformation("[{Pid}] {Wol}, {Kl}: usunięte z redis", Environment.ProcessId, kluczWolnychGodzin, kluczRezerwacjiKlienta);

            return new(rezerwacja);
        }
        finally
        {
            await _blokada.ZwolnijBlokadeAsync(kluczBlokady, token);
        }
    }

    private async Task<List<Rezerwacja>> PobierzRezerwacjeKlientaZBazyAsync(string klientId)
    {
        var rezerwacjeKlienta = await _rezerwacje.Find(rezerwacja => rezerwacja.KlientId == klientId).ToListAsync();
        return rezerwacjeKlienta.OrderBy(rezerwacja => rezerwacja.Data).ThenBy(rezerwacja => rezerwacja.Godzina).ToList();
    }

    private async Task<List<string>> PoliczWolneGodzinyZBazyAsync(Lokal lokal, string pracownikId, string data)
    {
        var dzienTygodnia = DateOnly.ParseExact(data, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayOfWeek;
        var godzinyOtwarciaWDniu = lokal.Godziny.FirstOrDefault(godziny => godziny.Dzien == dzienTygodnia);
        if (godzinyOtwarciaWDniu is null)
        {
            return [];
        }

        var godzinaOtwarcia = TimeOnly.ParseExact(godzinyOtwarciaWDniu.Od, "HH:mm", CultureInfo.InvariantCulture).Hour;
        var godzinaZamkniecia = TimeOnly.ParseExact(godzinyOtwarciaWDniu.Do, "HH:mm", CultureInfo.InvariantCulture).Hour;

        var zajeteGodziny = await _rezerwacje
            .Find(rezerwacja => rezerwacja.LokalId == lokal.Id && rezerwacja.PracownikId == pracownikId && rezerwacja.Data == data)
            .Project(rezerwacja => rezerwacja.Godzina)
            .ToListAsync();

        var wolneGodziny = new List<string>();
        for (var godzina = godzinaOtwarcia; godzina < godzinaZamkniecia; godzina++)
        {
            var slot = $"{godzina:00}:00";
            if (!zajeteGodziny.Contains(slot))
            {
                wolneGodziny.Add(slot);
            }
        }

        return wolneGodziny;
    }
}
