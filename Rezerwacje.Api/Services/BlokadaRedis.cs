using Microsoft.Extensions.Options;
using Rezerwacje.Api.models;
using StackExchange.Redis;

namespace Rezerwacje.Api.Services;

public class BlokadaRedis
{
    private static readonly LuaScript SkryptZwolnieniaBlokadyLUA = LuaScript.Prepare(
        """
        if redis.call('GET', @key) == @token then
            return redis.call('DEL', @key)
        end
        return 0
        """);

    private readonly IConnectionMultiplexer _redis;
    private readonly UstawieniaCache _ustawienia;
    private readonly ILogger<BlokadaRedis> _log;

    public BlokadaRedis(
        IConnectionMultiplexer redis,
        IOptions<UstawieniaCache> ustawienia,
        ILogger<BlokadaRedis> log)
    {
        _redis = redis;
        _ustawienia = ustawienia.Value;
        _log = log;
    }

    public async Task<string?> SprobujZalozycBlokadeAsync(string kluczBlokady)
    {
        var bazaRedis = _redis.GetDatabase();
        var token = Guid.NewGuid().ToString("N");
        var czasBlokady = TimeSpan.FromMilliseconds(_ustawienia.CzasBlokadyMs);

        return await bazaRedis.StringSetAsync(kluczBlokady, token, czasBlokady, When.NotExists) ? token : null;
    }

    public async Task<string?> ZalozBlokadePetlaAsync(string kluczBlokady)
    {
        for (var proba = 1; proba <= _ustawienia.ProbyBlokadyRezerwacji; proba++)
        {
            var token = await SprobujZalozycBlokadeAsync(kluczBlokady);
            if (token is not null)
            {
                _log.LogInformation("[{Pid}] {Klucz}: blokada założona", Environment.ProcessId, kluczBlokady);
                return token;
            }

            _log.LogInformation("[{Pid}] {Klucz}: wisi blokada, czekam ({Proba}/{Proby})",
                Environment.ProcessId, kluczBlokady, proba, _ustawienia.ProbyBlokadyRezerwacji);

            if (proba < _ustawienia.ProbyBlokadyRezerwacji)
            {
                await Task.Delay(_ustawienia.PrzerwaMiedzyProbamiMs);
            }
        }

        _log.LogError("[{Pid}] {Klucz}: blokada nieudana po {Proby} próbach",
            Environment.ProcessId, kluczBlokady, _ustawienia.ProbyBlokadyRezerwacji);
        return null;
    }

    public async Task ZwolnijBlokadeAsync(string kluczBlokady, string token)
    {
        var bazaRedis = _redis.GetDatabase();
        var wynikSkryptu = (int)await bazaRedis.ScriptEvaluateAsync(
            SkryptZwolnieniaBlokadyLUA,
            new { key = (RedisKey)kluczBlokady, token });

        if (wynikSkryptu == 0)
        {
            _log.LogError("[{Pid}] {Klucz}: blokada wygasła przed zwolnieniem", Environment.ProcessId, kluczBlokady);
            return;
        }

        _log.LogInformation("[{Pid}] {Klucz}: blokada zwolniona", Environment.ProcessId, kluczBlokady);
    }
}
