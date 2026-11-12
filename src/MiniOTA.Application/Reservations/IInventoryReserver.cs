using MiniOTA.Domain;

namespace MiniOTA.Application.Reservations;

// IInventoryReserver.cs：原子扣庫存
public interface IInventoryReserver
{
    /// <summary>
    /// 對區間內「每一晚」原子地扣庫存。全部成功回傳 true。
    /// 任何一晚不足就回傳 false；此時呼叫端必須回滾交易（不可 Commit），
    /// 因為部分晚數可能已在交易內被扣除。
    /// 必須在已開啟的交易內呼叫。
    /// </summary>
    Task<bool> TryReserveAsync(
        int roomTypeId,
        StayDateRange stay,
        int rooms,
        CancellationToken cancellationToken);
}