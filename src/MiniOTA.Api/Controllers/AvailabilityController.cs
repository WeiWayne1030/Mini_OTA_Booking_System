using Microsoft.AspNetCore.Mvc;
using MiniOTA.Application.Availability;
using MiniOTA.Domain;

namespace MiniOTA.Api.Controllers;

[ApiController]
[Route("availability")]
public class AvailabilityController : ControllerBase
{
    private readonly QueryAvailabilityHandler _handler;

    public AvailabilityController(QueryAvailabilityHandler handler)
        => _handler = handler;

    [HttpGet]
    [ProducesResponseType(typeof(AvailabilityResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get(
        [FromQuery] DateOnly checkInDate,
        [FromQuery] DateOnly checkOutDate,
        [FromQuery] int roomCount,
        [FromQuery] int roomTypeId = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _handler.HandleAsync(
                new AvailabilityQuery(roomTypeId, checkInDate, checkOutDate, roomCount),
                cancellationToken);

            return Ok(result);
        }
        catch (DomainException ex) //handler 在房數小於等於 0、或退房日不晚於入住日時，會丟這個例外。
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid availability request",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }
}