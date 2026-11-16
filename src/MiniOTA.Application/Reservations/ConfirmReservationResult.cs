namespace MiniOTA.Application.Reservations;

public enum ConfirmReservationOutcome
{
    Confirmed,              // 確認成功
    AlreadyConfirmed,       // 已經確認過了
    NotFound,               // 找不到預約
    NotConfirmable,         // 無法確認（例如已過付款期限）
}

public sealed record ConfirmReservationResult(
    ConfirmReservationOutcome Outcome,
    Guid? ReservationId = null,
    DateTime? PaymentDeadlineUtc = null);