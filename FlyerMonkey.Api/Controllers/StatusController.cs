using Microsoft.AspNetCore.Mvc;

using FlyerMonkey.Api.Services;

namespace FlyerMonkey.Api.Controllers
{
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    [ApiController]
    [Route("api/[controller]")]
    public class StatusController(IConfiguration configuration, IWebHostEnvironment environment) : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            if (CustomerCraterAccess.IsLocalHttps(HttpContext, environment))
            {
                Response.Headers.CacheControl = "no-store";
                return Ok(new
                {
                    application = "FlyerMonkey.Api",
                    status = "OK",
                    androidGenerationReady = CustomerCraterAccess.PreviewEnabled(configuration) &&
                        CustomerCraterAccess.AllowedCustomer(configuration).HasValue &&
                        CustomerCraterAccess.GenerationEnabled(configuration) &&
                        configuration.GetValue<bool>("Authentication:Enabled") &&
                        configuration.GetValue<bool>("Authentication:ViewOwnOnly") &&
                        !configuration.GetValue<bool>("CraterIt:LocalDevelopmentGenerationEnabled"),
                    androidPreviewReady = CustomerCraterAccess.PreviewEnabled(configuration) &&
                        CustomerCraterAccess.AllowedCustomer(configuration).HasValue &&
                        !CustomerCraterAccess.GenerationEnabled(configuration) &&
                        configuration.GetValue<bool>("Authentication:Enabled") &&
                        configuration.GetValue<bool>("Authentication:ViewOwnOnly") &&
                        !configuration.GetValue<bool>("CraterIt:LocalDevelopmentGenerationEnabled")
                });
            }
            return Ok(new
            {
                application = "FlyerMonkey.Api",
                status = "OK"
            });
        }
    }
}
