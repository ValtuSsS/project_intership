using System.Globalization;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Rezerwacje.Api.models;

namespace Rezerwacje.Api.Services;

public class RezerwacjeService
{
    private readonly IMongoCollection<Rezerwacja> _rez;
    private readonly LokaleService _lokSrv;
    private readonly CacheRedis _cache;
    private readonly BlokadaRedis _blok;
    private readonly Cache _ust;
    private readonly ILogger<RezerwacjeService> _log;

    public RezerwacjeService(
        IMongoClient mongo,
        IOptions<BazaUstawienia> baza,
        IOptions<Cache> ust,
        LokaleService lokSrv,
        CacheRedis cache,
        BlokadaRedis blok,
        ILogger<RezerwacjeService> log)
    {
        _rez = mongo.GetDatabase(baza.Value.Nazwa).GetCollection<Rezerwacja>("rezerwacje");
        _lokSrv = lokSrv;
        _cache = cache;
        _blok = blok;
        _ust = ust.Value;
        _log = log;
    }

    public async Task<(bool Ok, List<string>? Wart)> WolneAsync(Lokal lok, string prId, string data) =>
        await _cache.PobierzAsync(
            $"wolne:{lok.Id}:{prId}:{data}",
            $"lock:{lok.Id}:{prId}:{data}",
            TimeSpan.FromSeconds(_ust.HarmonS),
            () => WolneZBazyAsync(lok, prId, data));

    public async Task<(bool Ok, List<Rezerwacja>? Wart)> MojeAsync(string klId) =>
        await _cache.PobierzAsync(
            $"rezerwacje:klient:{klId}",
            $"rebuild:klient:{klId}",
            TimeSpan.FromSeconds(_ust.RezerwTtlS),
            () => MojeZBazyAsync(klId));

    public async Task<WynikRezerwacji> RezerwujAsync(string klId, NowaRezerwacja nowa)
    {
        if (!Format.Data(nowa.Data) || !Format.Godzina(nowa.Godzina))
        {
            return new(null, "Data yyyy-MM-dd, godzina pełna HH:00", 400);
        }

        var blokKl = $"lock:{nowa.LokalId}:{nowa.PracownikId}:{nowa.Data}";
        var token = await _blok.PetlaAsync(blokKl);
        if (token is null)
        {
            return new(null, "Spróbuj ponownie", 503);
        }

        try
        {
            var lok = await _lokSrv.PobierzAsync(nowa.LokalId);
            if (lok is null || !lok.Pracownicy.Any(x => x.Id == nowa.PracownikId))
            {
                return new(null, "Nie ma takiego lokalu lub pracownika", 404);
            }

            var wolne = await WolneZBazyAsync(lok, nowa.PracownikId, nowa.Data);
            _log.LogInformation("[{Pid}] {Lok} {Prac} {Data}: cosmos, wolnych godzin {Ile}",
                Environment.ProcessId, nowa.LokalId, nowa.PracownikId, nowa.Data, wolne.Count);
            if (!wolne.Contains(nowa.Godzina))
            {
                _log.LogInformation("[{Pid}] {Lok} {Prac} {Data} {Godz}: termin zajęty",
                    Environment.ProcessId, nowa.LokalId, nowa.PracownikId, nowa.Data, nowa.Godzina);
                return new(null, "Termin zajęty lub poza godzinami otwarcia", 409);
            }

            var rez = new Rezerwacja
            {
                Id = $"{nowa.LokalId}|{nowa.PracownikId}|{nowa.Data}|{nowa.Godzina}",
                KlientId = klId,
                LokalId = nowa.LokalId,
                PracownikId = nowa.PracownikId,
                Data = nowa.Data,
                Godzina = nowa.Godzina,
                Utworzono = DateTime.UtcNow
            };

            try
            {
                await _rez.InsertOneAsync(rez);
            }
            catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                _log.LogWarning("[{Pid}] {Id}: duplikat _id", Environment.ProcessId, rez.Id);
                return new(null, "Termin zajęty", 409);
            }

            _log.LogInformation("[{Pid}] {Id}: cosmos, zapisana dla {Klient}", Environment.ProcessId, rez.Id, klId);

            var klWol = $"wolne:{nowa.LokalId}:{nowa.PracownikId}:{nowa.Data}";
            var klKl = $"rezerwacje:klient:{klId}";
            await _cache.UsunAsync(klWol, klKl);
            _log.LogInformation("[{Pid}] {Wol}, {Kl}: usunięte z redis", Environment.ProcessId, klWol, klKl);

            return new(rez);
        }
        finally
        {
            await _blok.ZwolnienieAsync(blokKl, token);
        }
    }

    private async Task<List<Rezerwacja>> MojeZBazyAsync(string klId)
    {
        var lista = await _rez.Find(x => x.KlientId == klId).ToListAsync();
        return lista.OrderBy(x => x.Data).ThenBy(x => x.Godzina).ToList();
    }

    private async Task<List<string>> WolneZBazyAsync(Lokal lok, string prId, string data)
    {
        var dzien = DateOnly.ParseExact(data, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayOfWeek;
        var godz = lok.Godziny.FirstOrDefault(x => x.Dzien == dzien);
        if (godz is null)
        {
            return [];
        }

        var od = TimeOnly.ParseExact(godz.Od, "HH:mm", CultureInfo.InvariantCulture).Hour;
        var zamk = TimeOnly.ParseExact(godz.Do, "HH:mm", CultureInfo.InvariantCulture).Hour;

        var zajete = await _rez
            .Find(x => x.LokalId == lok.Id && x.PracownikId == prId && x.Data == data)
            .Project(x => x.Godzina)
            .ToListAsync();

        var wolne = new List<string>();
        for (var h = od; h < zamk; h++)
        {
            var slot = $"{h:00}:00";
            if (!zajete.Contains(slot))
            {
                wolne.Add(slot);
            }
        }

        return wolne;
    }
}