using Microsoft.EntityFrameworkCore;
using MiniOTA.Application.Reservations;
using MiniOTA.Domain;

namespace MiniOTA.Infrastructure.Persistence;

public sealed class InventoryReserver : IInventoryReserver
{
    private readonly ApplicationDbContext _db;

    public InventoryReserver(ApplicationDbContext db) => _db = db;

    public async Task<bool> TryReserveAsync(
        int roomTypeId,
        StayDateRange stay,
        int rooms,
        CancellationToken cancellationToken)
    {
        // 檢查（available >= rooms）與扣除寫在同一個 UPDATE，由資料庫保證原子性。
        // 內插字串會被轉成參數，不會有 SQL injection。
        var affected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE daily_inventory
               SET available = available - {rooms},
                   reserved  = reserved  + {rooms}
             WHERE room_type_id = {roomTypeId}
               AND stay_date >= {stay.CheckIn}
               AND stay_date <  {stay.CheckOut}
               AND available >= {rooms}", cancellationToken);

        // 每一晚都必須成功更新一列，否則視為庫存不足
        return affected == stay.Nights;
    }

    public async Task<bool> TryConfirmAsync(
    int roomTypeId,
    StayDateRange stay,
    int rooms,
    CancellationToken cancellationToken)
    {
        // 把 Reserved 轉成 Confirmed（BR-007）。檢查（reserved >= rooms）與搬移寫在同一個 UPDATE。
        var affected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE daily_inventory
               SET reserved  = reserved  - {rooms},
                   confirmed = confirmed + {rooms}
             WHERE room_type_id = {roomTypeId}
               AND stay_date >= {stay.CheckIn}
               AND stay_date <  {stay.CheckOut}
               AND reserved >= {rooms}", cancellationToken);

        // 每一晚都必須成功搬移一列
        return affected == stay.Nights;
    }
}