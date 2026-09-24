using Microsoft.AspNetCore.Mvc;

namespace FlyerMonkey.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ImagesController : ControllerBase
{
    [HttpPost("generate")]
    public ActionResult Generate()
    {
        return Ok("☄️ Image generation endpoint reached.");
    }
}