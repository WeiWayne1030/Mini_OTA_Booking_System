using Microsoft.EntityFrameworkCore;
using MiniOTA.Domain;

namespace MiniOTA.Infrastructure.Persistence;

public static class InventorySeeder
{
    public static async Task SeedAsync(
        ApplicationDbContext db,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        const int roomTypeId = 1;
        const int totalRooms = 10;
        const int days = 90;

        var end = today.AddDays(days);

        var existingDates = (await db.DailyInventories
                .AsNoTracking()
                .Where(x => x.RoomTypeId == roomTypeId
                            && x.StayDate >= today
                            && x.StayDate < end)
                .Select(x => x.StayDate)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var missing = Enumerable.Range(0, days)
            .Select(today.AddDays)
            .Where(d => !existingDates.Contains(d))
            .Select(d => DailyInventory.Create(roomTypeId, d, totalRooms))
            .ToList();

        if (missing.Count == 0)
        {
            return;
        }

        db.DailyInventories.AddRange(missing);
        await db.SaveChangesAsync(cancellationToken);
    }
}