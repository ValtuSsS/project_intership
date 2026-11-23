using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Rezerwacje.Api.Filtry;

public class WymaganyKlientIdAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext kontekst)
    {
        var klientId = kontekst.HttpContext.Request.Headers["X-Klient-Id"].ToString();
        if (!string.IsNullOrWhiteSpace(klientId))
        {
            return;
        }

        var opisBledu = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            Title = "Bad Request",
            Status = StatusCodes.Status400BadRequest,
            Detail = "Brak klientId"
        };

        kontekst.Result = new ObjectResult(opisBledu)
        {
            StatusCode = StatusCodes.Status400BadRequest
        };
    }
}
