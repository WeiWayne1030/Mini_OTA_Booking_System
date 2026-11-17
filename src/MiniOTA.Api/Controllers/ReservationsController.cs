using Microsoft.AspNetCore.Mvc;
using MiniOTA.Api.Contracts;
using MiniOTA.Application.Reservations;
using MiniOTA.Domain;

namespace MiniOTA.Api.Controllers;

[ApiController]
[Route("reservations")]
public class ReservationsController : ControllerBase
{
    private readonly CreateReservationHandler _createHandler;
    private readonly ConfirmReservationHandler _confirmHandler;

    public ReservationsController(
        CreateReservationHandler createHandler,
        ConfirmReservationHandler confirmHandler)
    {
        _createHandler = createHandler;
        _confirmHandler = confirmHandler;
    }

    [HttpPost]
    [ProducesResponseType(typeof(CreateReservationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CreateReservationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateReservationRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromHeader(Name = "X-Client-Id")] string? clientId,
        CancellationToken cancellationToken)
    {
        var command = new CreateReservationCommand(
            ClientId: string.IsNullOrWhiteSpace(clientId) ? "anonymous" : clientId,
            IdempotencyKey: idempotencyKey ?? string.Empty,
            RoomTypeId: request.RoomTypeId,
            CheckInDate: request.CheckInDate,
            CheckOutDate: request.CheckOutDate,
            RoomCount: request.RoomCount);

        try
        {
            var result = await _createHandler.HandleAsync(command, cancellationToken);

            return result.Outcome switch
            {
                CreateReservationOutcome.Created =>
                    StatusCode(StatusCodes.Status201Created, ToResponse(result)),

                CreateReservationOutcome.Replayed =>
                    Ok(ToResponse(result)),

                CreateReservationOutcome.InsufficientInventory =>
                    Problem(
                        title: "Insufficient inventory",
                        detail: "At least one requested night does not have enough rooms.",
                        statusCode: StatusCodes.Status409Conflict),

                CreateReservationOutcome.IdempotencyKeyConflict =>
                    Problem(
                        title: "Idempotency key conflict",
                        detail: "This Idempotency-Key was already used with a different request.",
                        statusCode: StatusCodes.Status409Conflict),

                _ => throw new InvalidOperationException(
                    $"Unhandled outcome: {result.Outcome}")
            };
        }
        catch (DomainException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid reservation request",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    [HttpPost("{id:guid}/pay")]
    [ProducesResponseType(typeof(PayReservationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Pay(Guid id, CancellationToken cancellationToken)
    {
        var result = await _confirmHandler.HandleAsync(
            new ConfirmReservationCommand(id), cancellationToken);

        return result.Outcome switch
        {
            // 重複付款不是錯誤，和第一次付款回一樣的結果（付款是冪等的）
            ConfirmReservationOutcome.Confirmed or ConfirmReservationOutcome.AlreadyConfirmed =>
                Ok(new PayReservationResponse(
                    result.ReservationId!.Value,
                    nameof(ReservationStatus.Confirmed))),

            ConfirmReservationOutcome.NotFound =>
                Problem(
                    title: "Reservation not found",
                    detail: $"No reservation with id {id}.",
                    statusCode: StatusCodes.Status404NotFound),

            ConfirmReservationOutcome.NotConfirmable =>
                Problem(
                    title: "Reservation cannot be paid",
                    detail: "The reservation has expired, was cancelled, or its payment deadline has passed.",
                    statusCode: StatusCodes.Status409Conflict),

            _ => throw new InvalidOperationException($"Unhandled outcome: {result.Outcome}")
        };
    }

    // Created / Replayed 一定有編號與期限
    private static CreateReservationResponse ToResponse(CreateReservationResult result)
        => new(result.ReservationId!.Value, result.PaymentDeadlineUtc!.Value);
}