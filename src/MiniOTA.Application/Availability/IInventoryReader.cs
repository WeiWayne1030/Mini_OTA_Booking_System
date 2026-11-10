using MiniOTA.Domain;

namespace MiniOTA.Application.Availability;

public interface IInventoryReader
{
    Task<IReadOnlyList<DailyInventory>> GetRangeAsync(
        int roomTypeId,
        StayDateRange stay,
        CancellationToken cancellationToken);
}