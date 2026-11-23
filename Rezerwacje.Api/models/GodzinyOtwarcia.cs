namespace Rezerwacje.Api.models;

public class GodzinyOtwarcia
{
    public DayOfWeek Dzien { get; set; }
    public string Od {get; set;} = null!;
    public string Do {get; set;}= null! ;

}
