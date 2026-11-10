using MiniOTA.Domain;

namespace MiniOTA.Application.Availability;

public sealed class QueryAvailabilityHandler
{
    private readonly IInventoryReader _inventory;

    public QueryAvailabilityHandler(IInventoryReader inventory)
        => _inventory = inventory;

    public async Task<AvailabilityResult> HandleAsync(
        AvailabilityQuery query, CancellationToken ct)
    {
        if (query.RoomCount <= 0)
            throw new DomainException("RoomCount must be a positive integer.");

        var stay = new StayDateRange(query.CheckInDate, query.CheckOutDate);
        var rows = await _inventory.GetRangeAsync(query.RoomTypeId, stay, ct);
        var byDate = rows.ToDictionary(r => r.StayDate);

        var days = stay.StayDates()
            .Select(d => new DailyAvailability(
                d, byDate.TryGetValue(d, out var row) ? row.Available : 0))
            .ToList();

        return new AvailabilityResult(
            days.All(d => d.Available >= query.RoomCount), days);
    }
}