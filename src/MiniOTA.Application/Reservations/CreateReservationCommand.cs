namespace MiniOTA.Application.Reservations;

public sealed record CreateReservationCommand(
    string ClientId,
    string IdempotencyKey,
    int RoomTypeId,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    int RoomCount);