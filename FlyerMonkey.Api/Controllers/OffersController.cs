using FlyerMonkey.Shared.Model;
using Microsoft.AspNetCore.Mvc;
using SQLServerConnection.Data;

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
}