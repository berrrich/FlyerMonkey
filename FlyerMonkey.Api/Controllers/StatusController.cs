using Microsoft.AspNetCore.Mvc;

namespace FlyerMonkey.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StatusController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(new
            {
                application = "FlyerMonkey.Api",
                status = "OK"
            });
        }
    }
}