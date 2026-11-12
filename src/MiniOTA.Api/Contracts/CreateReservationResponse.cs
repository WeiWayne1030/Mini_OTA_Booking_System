namespace MiniOTA.Api.Contracts;

public sealed record CreateReservationResponse(
    Guid ReservationId,
    DateTime PaymentDeadlineUtc);