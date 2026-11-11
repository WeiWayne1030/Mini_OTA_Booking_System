namespace MiniOTA.Application.Reservations;

public sealed class ReservationOptions
{
    /// <summary>預約建立後，必須在多久內付款（SRS §11 待決項目，先用 15 分鐘）。</summary>
    public TimeSpan PaymentTimeout { get; init; } = TimeSpan.FromMinutes(15);
}