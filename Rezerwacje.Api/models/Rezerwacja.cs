using System;
using MongoDB.Bson.Serialization.Attributes;

namespace Rezerwacje.Api.models;

[BsonIgnoreExtraElements]
public class Rezerwacja
{
    [BsonId]
    public string Id { get; set; } = null!;

    public string KlientId { get; set; } = null!;

    public string LokalId { get; set; } = null!;

    public string PracownikId { get; set; } = null!;

    public string Data { get; set; } = null!;

    public string Godzina { get; set; } = null!;

    public DateTime Utworzono { get; set; }
}