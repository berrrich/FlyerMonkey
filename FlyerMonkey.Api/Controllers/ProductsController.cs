using FlyerMonkey.Api.Services;
using FlyerMonkey.Shared.Model;
using Microsoft.AspNetCore.Mvc;

namespace FlyerMonkey.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly ProductService _productService;

    public ProductsController(ProductService productService)
    {
        _productService = productService;
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

    [HttpPost]
    public async Task<ActionResult<Product>> Create(
        Product product,
        CancellationToken cancellationToken)
    {
        var id =
            await _productService.AddAsync(
                product,
                cancellationToken);

        product.ID = id;

        return CreatedAtAction(
            nameof(Get),
            new { id = product.ID },
            product);
    }
}