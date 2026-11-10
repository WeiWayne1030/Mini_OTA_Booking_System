using Microsoft.EntityFrameworkCore;
using MiniOTA.Application.Availability;
using MiniOTA.Domain;

namespace MiniOTA.Infrastructure.Persistence;

public sealed class InventoryReader : IInventoryReader
{
    private readonly ApplicationDbContext _db;

    public InventoryReader(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<DailyInventory>> GetRangeAsync(
        int roomTypeId,
        StayDateRange stay,
        CancellationToken cancellationToken)
    {
        //退房日不佔用庫存
        return await _db.DailyInventories
            .AsNoTracking()
            .Where(x => x.RoomTypeId == roomTypeId
                        && x.StayDate >= stay.CheckIn
                        && x.StayDate < stay.CheckOut)
            .OrderBy(x => x.StayDate)
            .ToListAsync(cancellationToken);
    }
}