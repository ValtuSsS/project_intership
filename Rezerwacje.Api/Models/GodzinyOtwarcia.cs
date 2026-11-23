namespace Rezerwacje.Api.Models;

public class GodzinyOtwarcia
{
    public DayOfWeek DzienTygodnia { get; set; }

    public string OdGodziny { get; set; } = null!;

    public string DoGodziny { get; set; } = null!;
}
