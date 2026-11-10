namespace MiniOTA.Application.Availability;

public sealed record AvailabilityQuery(
    int RoomTypeId,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    int RoomCount);