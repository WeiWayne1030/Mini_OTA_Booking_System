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
        // 1. 開交易(參考 CreateReservationHandler.TryCreateAsync 的第一行)
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
            // 待付款的預約，庫存應該一定在 Reserved 裡。轉不動代表資料不一致，
            // 這不是客戶端的錯，所以丟例外；沒有 Commit，交易會回滾。
            throw new InvalidOperationException(
                $"Reservation {reservation.Id} is pending, but its inventory could not be moved from Reserved to Confirmed.");
        }

        // 5. 儲存並提交
        await _unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new ConfirmReservationResult(
            ConfirmReservationOutcome.Confirmed, reservation.Id);
    }
}
