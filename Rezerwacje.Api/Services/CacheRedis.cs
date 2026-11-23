using System.Text.Json;
using Microsoft.Extensions.Options;
using Rezerwacje.Api.models;
using StackExchange.Redis;

namespace Rezerwacje.Api.Services;

public class CacheRedis
{
    private readonly IConnectionMultiplexer _redis;
    private readonly BlokadaRedis _blok;
    private readonly Cache _ust;
    private readonly ILogger<CacheRedis> _log;

    public CacheRedis(
        IConnectionMultiplexer redis,
        BlokadaRedis blok,
        IOptions<Cache> ust,
        ILogger<CacheRedis> log)
    {
        _redis = redis;
        _blok = blok;
        _ust = ust.Value;
        _log = log;
    }

    public async Task<(bool Ok, T? Wart)> PobierzAsync<T>(
        string klucz,
        string blokadaKl,
        TimeSpan ttl,
        Func<Task<T>> przep)
    {
        var db = _redis.GetDatabase();

        for (var i = 1; i <= _ust.ProbyOdczyt; i++)
        {
            var json = await db.StringGetAsync(klucz);
            if (json.HasValue)
            {
                _log.LogInformation("[{Pid}] {Klucz}: redis", Environment.ProcessId, klucz);
                return (true, JsonSerializer.Deserialize<T>(json.ToString()));
            }

            var token = await _blok.BlokadaAsync(blokadaKl);
            if (token is not null)
            {
                try
                {
                    json = await db.StringGetAsync(klucz);
                    if (json.HasValue)
                    {
                        _log.LogInformation("[{Pid}] {Klucz}: przed chwila zostal utworzony", Environment.ProcessId, klucz);
                        return (true, JsonSerializer.Deserialize<T>(json.ToString()));
                    }

                    if (_ust.OpoznienieMs > 0)
                    {
                        _log.LogInformation("[{Pid}] {Klucz}: buduję, opóźnienie {Ms} ms", Environment.ProcessId, klucz, _ust.OpoznienieMs);
                        await Task.Delay(_ust.OpoznienieMs);
                    }

                    var wart = await przep();
                    await db.StringSetAsync(klucz, JsonSerializer.Serialize(wart), ttl);

                    _log.LogInformation("[{Pid}] {Klucz}: cosmos", Environment.ProcessId, klucz);
                    return (true, wart);
                }
                finally
                {
                    await _blok.ZwolnienieAsync(blokadaKl, token);
                }
            }

            _log.LogInformation("[{Pid}] {Klucz}: wisi blokada, czekam", Environment.ProcessId, klucz);

            if (i < _ust.ProbyOdczyt)
            {
                await Task.Delay(_ust.PrzerwaMs);
            }
        }

        _log.LogError("[{Pid}] {Klucz}: blokada {Blok} nieudana po {Proby} próbach",
            Environment.ProcessId, klucz, blokadaKl, _ust.ProbyOdczyt);
        return (false, default);
    }

    public async Task UsunAsync(params string[] klucze)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(klucze.Select(k => (RedisKey)k).ToArray());
    }
}