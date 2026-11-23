using Microsoft.Extensions.Options;
using Rezerwacje.Api.models;
using StackExchange.Redis;

namespace Rezerwacje.Api.Services;

public class BlokadaRedis
{
    private static readonly LuaScript SkryptLUA = LuaScript.Prepare(
        """
        if redis.call('GET', @key) == @token then
            return redis.call('DEL', @key)
        end
        return 0
        """);

    private readonly IConnectionMultiplexer _redis;
    private readonly Cache _ust;
    private readonly ILogger<BlokadaRedis> _log;

    public BlokadaRedis(
        IConnectionMultiplexer redis,
        IOptions<Cache> ust,
        ILogger<BlokadaRedis> log)
    {
        _redis = redis;
        _ust = ust.Value;
        _log = log;
    }

    public async Task<string?> BlokadaAsync(string klucz)
    {
        var db = _redis.GetDatabase();
        var token = Guid.NewGuid().ToString("N");
        var ttl = TimeSpan.FromMilliseconds(_ust.KluczTtlMls);

        return await db.StringSetAsync(klucz, token, ttl, When.NotExists) ? token : null;
    }

    public async Task<string?> PetlaAsync(string klucz)
    {
        for (var i = 1; i <= _ust.Proby; i++)
        {
            var token = await BlokadaAsync(klucz);
            if (token is not null)
            {
                _log.LogInformation("[{Pid}] {Klucz}: blokada założona", Environment.ProcessId, klucz);
                return token;
            }

            _log.LogInformation("[{Pid}] {Klucz}: wisi blokada, czekam ({Proba}/{Proby})",
                Environment.ProcessId, klucz, i, _ust.Proby);

            if (i < _ust.Proby)
            {
                await Task.Delay(_ust.PrzerwaMs);
            }
        }

        _log.LogError("[{Pid}] {Klucz}: blokada nieudana po {Proby} próbach", Environment.ProcessId, klucz, _ust.Proby);
        return null;
    }

    public async Task ZwolnienieAsync(string klucz, string token)
    {
        var db = _redis.GetDatabase();
        var wynik = (int)await db.ScriptEvaluateAsync(SkryptLUA, new { key = (RedisKey)klucz, token });

        if (wynik == 0)
        {
            _log.LogError("[{Pid}] {Klucz}: blokada wygasła przed zwolnieniem", Environment.ProcessId, klucz);
            return;
        }

        _log.LogInformation("[{Pid}] {Klucz}: blokada zwolniona", Environment.ProcessId, klucz);
    }
}