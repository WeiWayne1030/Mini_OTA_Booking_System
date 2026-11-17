namespace MiniOTA.Application.Reservations;

/// <summary>
/// 寫入時發現另一個請求搶先改了同一筆預約或其庫存。
/// 目前的交易必須回滾；呼叫端應重新讀取最新狀態再決定回覆。
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(Exception? inner = null)
        : base("The reservation or its inventory was modified by another request.", inner)
    {
    }
}