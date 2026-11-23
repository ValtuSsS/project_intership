using System.Text.Json;
using Microsoft.Extensions.Options;
using Rezerwacje.Api.models;
using StackExchange.Redis;

namespace Rezerwacje.Api.Services;

public class CacheRedis
{
    private readonly IConnectionMultiplexer _redis;
    private readonly BlokadaRedis _blokada;
    private readonly UstawieniaCache _ustawienia;
    private readonly ILogger<CacheRedis> _log;

    public CacheRedis(
        IConnectionMultiplexer redis,
        BlokadaRedis blokada,
        IOptions<UstawieniaCache> ustawienia,
        ILogger<CacheRedis> log)
    {
        _redis = redis;
        _blokada = blokada;
        _ustawienia = ustawienia.Value;
        _log = log;
    }

    public async Task<(bool CzyPobrano, T? Dane)> PobierzZCacheLubBazyAsync<T>(
        string kluczDanych,
        string kluczBlokady,
        TimeSpan czasZyciaCache,
        Func<Task<T>> pobierzZBazy)
    {
        var bazaRedis = _redis.GetDatabase();

        for (var proba = 1; proba <= _ustawienia.ProbyOdczytuCache; proba++)
        {
            var wartoscZRedisa = await bazaRedis.StringGetAsync(kluczDanych);
            if (wartoscZRedisa.HasValue)
            {
                _log.LogInformation("[{Pid}] {Klucz}: redis", Environment.ProcessId, kluczDanych);
                return (true, JsonSerializer.Deserialize<T>(wartoscZRedisa.ToString()));
            }

            var token = await _blokada.SprobujZalozycBlokadeAsync(kluczBlokady);
            if (token is not null)
            {
                try
                {
                    wartoscZRedisa = await bazaRedis.StringGetAsync(kluczDanych);
                    if (wartoscZRedisa.HasValue)
                    {
                        _log.LogInformation("[{Pid}] {Klucz}: przed chwila zostal utworzony", Environment.ProcessId, kluczDanych);
                        return (true, JsonSerializer.Deserialize<T>(wartoscZRedisa.ToString()));
                    }

                    if (_ustawienia.TestoweOpoznienieMs > 0)
                    {
                        _log.LogInformation("[{Pid}] {Klucz}: buduję, opóźnienie {Ms} ms",
                            Environment.ProcessId, kluczDanych, _ustawienia.TestoweOpoznienieMs);
                        await Task.Delay(_ustawienia.TestoweOpoznienieMs);
                    }

                    var daneZBazy = await pobierzZBazy();
                    await bazaRedis.StringSetAsync(kluczDanych, JsonSerializer.Serialize(daneZBazy), czasZyciaCache);

                    _log.LogInformation("[{Pid}] {Klucz}: baza", Environment.ProcessId, kluczDanych);
                    return (true, daneZBazy);
                }
                finally
                {
                    await _blokada.ZwolnijBlokadeAsync(kluczBlokady, token);
                }
            }

            _log.LogInformation("[{Pid}] {Klucz}: wisi blokada, czekam", Environment.ProcessId, kluczDanych);

            if (proba < _ustawienia.ProbyOdczytuCache)
            {
                await Task.Delay(_ustawienia.PrzerwaMiedzyProbamiMs);
            }
        }

        _log.LogError("[{Pid}] {Klucz}: blokada {Blok} nieudana po {Proby} próbach",
            Environment.ProcessId, kluczDanych, kluczBlokady, _ustawienia.ProbyOdczytuCache);
        return (false, default);
    }

    public async Task UsunZCacheAsync(params string[] klucze)
    {
        var bazaRedis = _redis.GetDatabase();
        await bazaRedis.KeyDeleteAsync(klucze.Select(klucz => (RedisKey)klucz).ToArray());
    }
}
