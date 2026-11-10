namespace MiniOTA.Application.Availability;

public sealed record DailyAvailability(DateOnly StayDate, int Available);

public sealed record AvailabilityResult(
    bool IsAvailable,
    IReadOnlyList<DailyAvailability> Days);