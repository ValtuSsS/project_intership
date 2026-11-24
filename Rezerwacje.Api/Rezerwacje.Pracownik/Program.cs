
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

var argumenty = args;

if (argumenty.Length != 2)
{
    Console.WriteLine("Podaj identyfikator lokalu i pracownika.");
    Console.WriteLine("Przykład: dotnet run -- lok5 p1");
    return;
}

var lokalId = argumenty[0];
var pracownikId = argumenty[1];

var zawartoscUstawien = await File.ReadAllTextAsync("appsettings.json");
using var dokumentUstawien = JsonDocument.Parse(zawartoscUstawien);

var ustawienia = dokumentUstawien.RootElement;

var host = ustawienia.GetProperty("Host").GetString();
var nazwaExchange = ustawienia.GetProperty("NazwaExchange").GetString();
var gotowe = ustawienia.GetProperty("Gotowe").GetInt32();

if (gotowe != 1)
{
    Console.WriteLine("System lokalu wyłączony");
    return;
}

var kluczKolejki = $"{lokalId}.{pracownikId}";
var nazwaKolejki = $"rezerwacje.{kluczKolejki}";

var fabrykaPolaczenia = new ConnectionFactory
{
    HostName = host!
};

await using var polaczenie = await fabrykaPolaczenia.CreateConnectionAsync();
await using var kanal = await polaczenie.CreateChannelAsync();

await kanal.ExchangeDeclareAsync(
    exchange: nazwaExchange!,
    type: ExchangeType.Direct,
    durable: true,
    autoDelete: false,
    arguments: null);

await kanal.QueueDeclareAsync(
    queue: nazwaKolejki,
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: null);

await kanal.QueueBindAsync(
    queue: nazwaKolejki,
    exchange: nazwaExchange!,
    routingKey: kluczKolejki);

await kanal.BasicQosAsync(
    prefetchSize: 0,
    prefetchCount: 1,
    global: false);

var konsument = new AsyncEventingBasicConsumer(kanal);

konsument.ReceivedAsync += async (nadawca, wiadomosc) =>
{
    var rezerwacja = JsonSerializer.Deserialize<Rezerwacja>(wiadomosc.Body.Span);

    if (rezerwacja is null)
    {
        Console.WriteLine("Nie udało się odczytać rezerwacji.");
        return;
    }

    Console.WriteLine();
    Console.WriteLine("Otrzymano nową rezerwację:");
    Console.WriteLine($"Id rezerwacji: {rezerwacja.Id}");
    Console.WriteLine($"Klient: {rezerwacja.KlientId}");
    Console.WriteLine($"Lokal: {rezerwacja.LokalId}");
    Console.WriteLine($"Pracownik: {rezerwacja.PracownikId}");
    Console.WriteLine($"Data: {rezerwacja.Data}");
    Console.WriteLine($"Godzina: {rezerwacja.Godzina}");
    Console.WriteLine($"Data utworzenia: {rezerwacja.DataUtworzenia}");
    Console.WriteLine();

    await kanal.BasicAckAsync(
        deliveryTag: wiadomosc.DeliveryTag,
        multiple: false);
};

await kanal.BasicConsumeAsync(
    queue: nazwaKolejki,
    autoAck: false,
    consumer: konsument);

Console.WriteLine($"System lokalu uruchomiony dla {lokalId}, pracownik {pracownikId}.");
Console.WriteLine($"Nasłuchiwanie kolejki: {nazwaKolejki}");
Console.WriteLine("Naciśnij Enter, aby wyłączyć system lokalu.");

await Console.In.ReadLineAsync();

public class Rezerwacja
{
    public string Id { get; set; } = null!;

    public string KlientId { get; set; } = null!;

    public string LokalId { get; set; } = null!;

    public string PracownikId { get; set; } = null!;

    public string Data { get; set; } = null!;

    public string Godzina { get; set; } = null!;

    public DateTime DataUtworzenia { get; set; }
}
