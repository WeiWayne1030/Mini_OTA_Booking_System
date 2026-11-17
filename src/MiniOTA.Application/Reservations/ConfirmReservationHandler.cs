using MiniOTA.Domain;

namespace MiniOTA.Application.Reservations;
public sealed class ConfirmReservationHandler
{
    private readonly IReservationRepository _reservations;
    private readonly IInventoryReserver _inventory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public ConfirmReservationHandler(
    IReservationRepository reservations,
    IInventoryReserver inventory,
    IUnitOfWork unitOfWork,
    TimeProvider time)
    {
        _reservations = reservations;
        _inventory = inventory;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task<ConfirmReservationResult> HandleAsync(
     ConfirmReservationCommand command, CancellationToken ct)
    {
        try
        {
            return await TryConfirmAsync(command, ct);
        }
        catch (ConcurrencyConflictException)
        {
            // 另一個請求搶先改了這筆預約或它的庫存。我們的交易在離開 TryConfirmAsync 時已經回滾。
            // 先丟掉記憶體裡過時的狀態，再重新讀取「現在」的真實狀態來決定回覆。
            _unitOfWork.ClearTrackedChanges();
            return await ResolveAfterConflictAsync(command.ReservationId, ct);
        }
    }

    private async Task<ConfirmReservationResult> TryConfirmAsync(
        ConfirmReservationCommand command, CancellationToken ct)
    {
        // 1. 開交易
        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);

        // 2. 用編號取出預約；找不到就直接回報 NotFound
        var reservation = await _reservations.GetAsync(command.ReservationId, ct);
        if (reservation is null)
        {
            return new ConfirmReservationResult(ConfirmReservationOutcome.NotFound);
        }

        // 已經確認過：付款是冪等的，重複付款不是錯誤，也不能再轉一次庫存
        if (reservation.Status == ReservationStatus.Confirmed)
        {
            return new ConfirmReservationResult(
                ConfirmReservationOutcome.AlreadyConfirmed, reservation.Id);
        }

        // 3. 已過期、已取消、超過期限,由 Domain 判斷
        var nowUtc = _time.GetUtcNow().UtcDateTime;
        try
        {
            reservation.Confirm(nowUtc);
        }
        catch (InvalidReservationTransitionException)
        {
            return new ConfirmReservationResult(
                ConfirmReservationOutcome.NotConfirmable, reservation.Id);
        }

        // 4. 轉庫存：Reserved → Confirmed
        var converted = await _inventory.TryConfirmAsync(
            reservation.RoomTypeId, reservation.Stay, reservation.RoomCount, ct);

        if (!converted)
        {
            // 同時付款時，輸家會在這裡發現庫存已被贏家轉走。
            // 丟出衝突讓交易回滾，由外層重新判斷到底是哪一種情況。
            throw new ConcurrencyConflictException();
        }

        // 5. 儲存並提交（若預約已被別人改過，xmin 檢查會讓這裡丟出衝突）
        await _unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new ConfirmReservationResult(
            ConfirmReservationOutcome.Confirmed, reservation.Id);
    }

    // 衝突之後，重新讀取預約「現在」的真實狀態，決定該回什麼
    private async Task<ConfirmReservationResult> ResolveAfterConflictAsync(
        Guid reservationId, CancellationToken ct)
    {
        var current = await _reservations.GetAsync(reservationId, ct);

        return current?.Status switch
        {
            // 贏家已經確認了：對輸家而言等同「重複付款」
            ReservationStatus.Confirmed =>
                new ConfirmReservationResult(ConfirmReservationOutcome.AlreadyConfirmed, current.Id),

            // 等待期間被取消或過期了
            ReservationStatus.Cancelled or ReservationStatus.Expired =>
                new ConfirmReservationResult(ConfirmReservationOutcome.NotConfirmable, current.Id),

            null =>
                new ConfirmReservationResult(ConfirmReservationOutcome.NotFound),

            // 預約仍是待付款，庫存卻轉不動：這才是真正的資料不一致，不是競爭
            _ => throw new InvalidOperationException(
                $"Reservation {reservationId} is still pending after a payment conflict; " +
                "its inventory could not be moved from Reserved to Confirmed.")
        };
    }
}
