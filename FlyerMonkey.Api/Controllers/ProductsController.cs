using FlyerMonkey.Api.Services;
using FlyerMonkey.Shared.Model;
using Microsoft.AspNetCore.Mvc;

namespace FlyerMonkey.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly ProductService _productService;
    private readonly ProductImageService _productImageService;

    public ProductsController(
        ProductService productService,
        ProductImageService productImageService)
    {
        _productService = productService;
        _productImageService = productImageService;
    }
    [HttpGet]
    public async Task<ActionResult<List<Product>>> GetAll(
        CancellationToken cancellationToken)
    {
        var products =
            await _productService.GetAllAsync(cancellationToken);

        return Ok(products);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Product>> Get(
        int id,
        CancellationToken cancellationToken)
    {
        var product =
            await _productService.GetAsync(id, cancellationToken);

        if (product == null)
        {
            return NotFound();
        }

        return Ok(product);
    }

    [HttpGet("{id}/image")]
    public async Task<IActionResult> GetImage(
    int id,
    CancellationToken cancellationToken)
    {
        var product =
            await _productService.GetAsync(id, cancellationToken);

        if (product == null ||
            string.IsNullOrWhiteSpace(product.ImageBlobPath))
        {
            return NotFound();
        }

        var imageStream =
            await _productImageService.GetImageAsync(
                product.ImageBlobPath,
                cancellationToken);

        if (imageStream == null)
        {
            return NotFound();
        }

        return File(imageStream, "image/jpeg");
    }

    // Catalog API is read-only. Administrative writes use the Windows Reviewer's
    // direct SQL/Blob workflow; do not expose that privilege through this API.
}
