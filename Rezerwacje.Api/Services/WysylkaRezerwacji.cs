
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Rezerwacje.Api.Models;
using System.Text.Json;

namespace Rezerwacje.Api.Services;

public class WysylkaRezerwacji : IAsyncDisposable
{
    private readonly UstawieniaRabbitMq _ustawienia;
    private readonly ILogger<WysylkaRezerwacji> _log;
    private readonly SemaphoreSlim _blokadaPolaczenia = new(1, 1);

    private IConnection? _polaczenie;
    private IChannel? _kanal;

    public WysylkaRezerwacji(
        IOptions<UstawieniaRabbitMq> ustawienia,
        ILogger<WysylkaRezerwacji> log)
    {
        _ustawienia = ustawienia.Value;
        _log = log;
    }

    public async Task WyslijRezerwacjeAsync(Rezerwacja rezerwacja)
    {
        await _blokadaPolaczenia.WaitAsync();

        try
        {
            await PrzygotujPolaczenieAsync();

            var kluczKolejki = $"{rezerwacja.LokalId}.{rezerwacja.PracownikId}";
            var nazwaKolejki = $"rezerwacje.{kluczKolejki}";

            await _kanal!.ExchangeDeclareAsync(
                exchange: _ustawienia.NazwaExchange,
                type: ExchangeType.Direct,
                durable: true,
                autoDelete: false,
                arguments: null);

            await _kanal.QueueDeclareAsync(
                queue: nazwaKolejki,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            await _kanal.QueueBindAsync(
                queue: nazwaKolejki,
                exchange: _ustawienia.NazwaExchange,
                routingKey: kluczKolejki);

            var trescWiadomosci = JsonSerializer.SerializeToUtf8Bytes(rezerwacja);

            var wlasciwosciWiadomosci = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent
            };

            await _kanal.BasicPublishAsync(
                exchange: _ustawienia.NazwaExchange,
                routingKey: kluczKolejki,
                mandatory: false,
                basicProperties: wlasciwosciWiadomosci,
                body: trescWiadomosci);

            _log.LogInformation(
                "[{Pid}] {LokalId}|{PracownikId}|{Data}|{Godzina}: wysłana do kolejki {NazwaKolejki}",
                Environment.ProcessId,
                rezerwacja.LokalId,
                rezerwacja.PracownikId,
                rezerwacja.Data,
                rezerwacja.Godzina,
                nazwaKolejki);
        }
        finally
        {
            _blokadaPolaczenia.Release();
        }
    }

    private async Task PrzygotujPolaczenieAsync()
    {
        if (_polaczenie is null || !_polaczenie.IsOpen)
        {
            var fabrykaPolaczenia = new ConnectionFactory
            {
                HostName = _ustawienia.Host
            };

            _polaczenie = await fabrykaPolaczenia.CreateConnectionAsync();
            _kanal = null;
        }

        if (_kanal is null || !_kanal.IsOpen)
        {
            _kanal = await _polaczenie.CreateChannelAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_kanal is not null)
        {
            await _kanal.DisposeAsync();
        }

        if (_polaczenie is not null)
        {
            await _polaczenie.DisposeAsync();
        }

        _blokadaPolaczenia.Dispose();
    }
}
