namespace MiniOTA.Api.Contracts;

public sealed record CreateReservationRequest(
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    int RoomCount,
    int RoomTypeId = 1);