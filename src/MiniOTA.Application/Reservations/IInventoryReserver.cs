using MiniOTA.Domain;

namespace MiniOTA.Application.Reservations;

// IInventoryReserver.cs：原子扣庫存
public interface IInventoryReserver
{
    /// <summary>
    /// 對區間內「每一晚」原子地扣庫存。全部成功回傳 true；
    /// 任何一晚不足就回傳 false，且不得改動任何一晚（全有或全無）。
    /// </summary>
    Task<bool> TryReserveAsync(
        int roomTypeId,
        StayDateRange stay,
        int rooms,
        CancellationToken cancellationToken);
}