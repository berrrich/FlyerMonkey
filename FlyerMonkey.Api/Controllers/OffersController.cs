using FlyerMonkey.Shared.Model;
using Microsoft.AspNetCore.Mvc;
using FlyerMonkey.Shared.Services;

namespace FlyerMonkey.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OffersController : ControllerBase
{
    private readonly IOfferRepository _offerRepository;

    public OffersController(IOfferRepository offerRepository)
    {
        _offerRepository = offerRepository;
    }

    [HttpGet]
    public async Task<ActionResult<List<OfferSummary>>> GetOffers(
        CancellationToken cancellationToken)
    {
        var offers =
            await _offerRepository.GetOfferSummariesAsync(cancellationToken);

        return Ok(offers);
    }

    [HttpGet("current")]
    public async Task<ActionResult<List<OfferSummary>>> GetCurrentOffers(
        CancellationToken cancellationToken)
    {
        var offers =
            await _offerRepository.GetCurrentOfferSummariesAsync(cancellationToken);

        return Ok(offers);
    }

    [HttpGet("previous")]
    public async Task<ActionResult<List<OfferSummary>>> GetPreviousOffers(
        CancellationToken cancellationToken)
    {
        var offers =
            await _offerRepository.GetPreviousOfferSummariesAsync(cancellationToken);

        return Ok(offers);
    }
}