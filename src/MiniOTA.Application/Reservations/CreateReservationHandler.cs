using System.Security.Cryptography;
using System.Text;
using MiniOTA.Domain;

namespace MiniOTA.Application.Reservations;

public sealed class CreateReservationHandler
{
    private readonly IReservationRepository _reservations;
    private readonly IInventoryReserver _inventory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;
    private readonly ReservationOptions _options;

    public CreateReservationHandler(
        IReservationRepository reservations,
        IInventoryReserver inventory,
        IUnitOfWork unitOfWork,
        TimeProvider time,
        ReservationOptions options)
    {
        _reservations = reservations;
        _inventory = inventory;
        _unitOfWork = unitOfWork;
        _time = time;
        _options = options;
    }

    public async Task<CreateReservationResult> HandleAsync(
        CreateReservationCommand command, CancellationToken ct)
    {
        // 1. 驗證輸入（BR-002、BR-003、BR-018）
        if (string.IsNullOrWhiteSpace(command.ClientId)
            || string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new DomainException("An idempotency key is required.");
        }

        if (command.RoomCount <= 0)
        {
            throw new DomainException("RoomCount must be a positive integer.");
        }

        var stay = new StayDateRange(command.CheckInDate, command.CheckOutDate);
        var requestHash = ComputeRequestHash(command);

        // 之後任何一條路徑直接 return，交易沒有 Commit 就會自動回滾
        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);

        // 2. 冪等檢查（FR-007、BR-019）
        var existing = await _reservations.FindIdempotencyAsync(
            command.ClientId, command.IdempotencyKey, ct);

        if (existing is not null)
        {
            if (existing.RequestHash != requestHash)
            {
                return new CreateReservationResult(CreateReservationOutcome.IdempotencyKeyConflict);
            }

            var original = await _reservations.GetAsync(existing.ReservationId, ct)
                ?? throw new InvalidOperationException(
                    $"Reservation {existing.ReservationId} referenced by idempotency record was not found.");

            return new CreateReservationResult(
                CreateReservationOutcome.Replayed,
                original.Id,
                original.PaymentDeadlineUtc);
        }

        // 3. 原子扣庫存：每一晚都成功，或全部不動（BR-010）
        var reserved = await _inventory.TryReserveAsync(
            command.RoomTypeId, stay, command.RoomCount, ct);

        if (!reserved)
        {
            return new CreateReservationResult(CreateReservationOutcome.InsufficientInventory);
        }

        // 4. 建立預約與冪等紀錄，與扣庫存放在同一個交易（BR-011）
        var now = _time.GetUtcNow().UtcDateTime;
        var reservation = Reservation.CreatePending(
            command.RoomTypeId, stay, command.RoomCount, now, _options.PaymentTimeout);

        _reservations.Add(reservation);
        _reservations.Add(IdempotencyRecord.Create(
            command.ClientId, command.IdempotencyKey, requestHash, reservation.Id, now));

        await _unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new CreateReservationResult(
            CreateReservationOutcome.Created,
            reservation.Id,
            reservation.PaymentDeadlineUtc);
    }

    // 只納入「影響預約內容」的欄位；冪等鍵本身與 ClientId 不算
    private static string ComputeRequestHash(CreateReservationCommand c)
    {
        var canonical =
            $"{c.RoomTypeId}|{c.CheckInDate:yyyy-MM-dd}|{c.CheckOutDate:yyyy-MM-dd}|{c.RoomCount}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}