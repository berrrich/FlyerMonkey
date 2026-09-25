using FlyerMonkey.Api.Models;
using FlyerMonkey.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FlyerMonkey.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ImagesController : ControllerBase
{
    private readonly ImageGenerationService _imageGenerationService;

    public ImagesController(ImageGenerationService imageGenerationService)
    {
        _imageGenerationService = imageGenerationService;
    }

    [HttpPost("generate")]
    public async Task<ActionResult> Generate(GenerateImageRequest request)
    {
        var result =
            await _imageGenerationService.GenerateAsync(request.Prompt);

        return Ok(result);
    }
}