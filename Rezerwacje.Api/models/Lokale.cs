using MongoDB.Bson.Serialization.Attributes;

namespace Rezerwacje.Api.models;

[BsonIgnoreExtraElements]
public class Lokal
{
    [BsonId]
    public string Id { get; set; } = null!;

    public string Nazwa { get; set; } = null!;

    public string Typ { get; set; } = null!;

    public List<Pracownik> Pracownicy { get; set; } = [];

    public List<GodzinyOtw> Godziny { get; set; } = [];
}