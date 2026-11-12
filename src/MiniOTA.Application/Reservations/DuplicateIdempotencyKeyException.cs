namespace MiniOTA.Application.Reservations;

/// <summary>
/// 寫入時發現相同 (ClientId, IdempotencyKey) 已被另一個請求搶先建立。
/// 由 Infrastructure 丟出，handler 接住後改回傳先前請求的結果。
/// </summary>
public sealed class DuplicateIdempotencyKeyException : Exception
{
    public DuplicateIdempotencyKeyException(Exception? inner = null)
        : base("An idempotency record with the same client and key already exists.", inner)
    {
    }
}