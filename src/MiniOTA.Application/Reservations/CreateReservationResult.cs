namespace MiniOTA.Application.Reservations;

public enum CreateReservationOutcome
{
    Created,                // 新建立
    Replayed,               // 相同冪等鍵、相同請求：回傳原本結果
    InsufficientInventory,  // 庫存不足
    IdempotencyKeyConflict  // 相同冪等鍵、但請求內容不同
}

public sealed record CreateReservationResult(
    CreateReservationOutcome Outcome,
    Guid? ReservationId = null,
    DateTime? PaymentDeadlineUtc = null);