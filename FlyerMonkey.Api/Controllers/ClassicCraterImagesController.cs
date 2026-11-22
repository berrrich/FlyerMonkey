using FlyerMonkey.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlyerMonkey.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/products/{productId:int:min(1)}/classic-crater-image")]
public sealed class ClassicCraterImagesController(IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public IActionResult Get(int productId)
    {
        if (!CustomerCraterAccess.IsLocalHttps(HttpContext, environment)) return NotFound();
        Response.Headers.CacheControl = "no-store";
        var artwork = HttpContext.RequestServices.GetRequiredService<ClassicCraterArtwork>();
        return artwork.Read(productId) is { } image ? File(image, "image/jpeg") : NotFound();
    }
}
