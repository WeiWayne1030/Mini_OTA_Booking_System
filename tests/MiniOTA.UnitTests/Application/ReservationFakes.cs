using MiniOTA.Application.Reservations;
using MiniOTA.Domain;

namespace MiniOTA.UnitTests.Application;

internal sealed class FakeReservationRepository : IReservationRepository
{
    public List<Reservation> Reservations { get; } = new();
    public List<IdempotencyRecord> Records { get; } = new();
    /// <summary>接下來有幾次冪等查詢要「裝作查不到」，模擬另一個請求尚未提交的瞬間。</summary>
    public int HiddenLookups { get; set; }

    public Task<IdempotencyRecord?> FindIdempotencyAsync(
        string clientId, string key, CancellationToken cancellationToken)
    {
        if (HiddenLookups > 0)
        {
            HiddenLookups--;
            return Task.FromResult<IdempotencyRecord?>(null);
        }

        return Task.FromResult(Records.FirstOrDefault(r => r.ClientId == clientId && r.Key == key));
    }

    /// <summary>覆寫查詢結果：參數是 (編號, 第幾次查詢)。用來模擬「回滾後重新查詢，資料庫顯示的是另一個狀態」。</summary>
    public Func<Guid, int, Reservation?>? GetOverride { get; set; }

    private int _getCalls;

    public Task<Reservation?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        _getCalls++;
        return GetOverride is not null
            ? Task.FromResult(GetOverride(id, _getCalls))
            : Task.FromResult(Reservations.FirstOrDefault(r => r.Id == id));
    }

    public void Add(Reservation reservation) => Reservations.Add(reservation);

    public void Add(IdempotencyRecord record) => Records.Add(record);
}

internal sealed class FakeInventoryReserver : IInventoryReserver
{
    /// <summary>設成 false 就模擬「庫存不足」。</summary>
    public bool Result { get; set; } = true;

    public int CallCount { get; private set; }

    public int ConfirmCallCount { get; private set; }

    public (int RoomTypeId, StayDateRange Stay, int Rooms)? LastCall { get; private set; }

    public Task<bool> TryReserveAsync(
        int roomTypeId, StayDateRange stay, int rooms, CancellationToken cancellationToken)
    {
        CallCount++;
        LastCall = (roomTypeId, stay, rooms);
        return Task.FromResult(Result);
    }

    /// <summary>在庫存轉換的瞬間執行的動作，用來模擬「這時別人剛好改了預約」。</summary>
    public Action? OnTryConfirm { get; set; }

    public Task<bool> TryConfirmAsync(
    int roomTypeId, StayDateRange stay, int rooms, CancellationToken cancellationToken)
    {
        ConfirmCallCount++;
        LastCall = (roomTypeId, stay, rooms);
        OnTryConfirm?.Invoke();
        return Task.FromResult(Result);
    }
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public List<FakeTransaction> Transactions { get; } = new();

    public int SaveCount { get; private set; }

    /// <summary>設成 true 就模擬「寫入資料庫時失敗」。</summary>
    public bool ThrowOnSave { get; set; }

    /// <summary>設成 true 就模擬「寫入時資料庫發現相同冪等鍵已存在」。</summary>
    public bool ThrowDuplicateOnSave { get; set; }

    public int ClearTrackedChangesCount { get; private set; }

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        var transaction = new FakeTransaction();
        Transactions.Add(transaction);
        return Task.FromResult<IUnitOfWorkTransaction>(transaction);
    }

    /// <summary>設成 true 就模擬「存檔時發現預約已被別人改過（xmin 衝突）」。</summary>
    public bool ThrowConflictOnSave { get; set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;

        if (ThrowConflictOnSave)
        {
            throw new ConcurrencyConflictException();
        }

        if (ThrowDuplicateOnSave)
        {
            throw new DuplicateIdempotencyKeyException();
        }

        if (ThrowOnSave)
        {
            throw new InvalidOperationException("Simulated database failure.");
        }

        return Task.CompletedTask;
    }

    //1.	輸家讀到預約（Pending），EF 把這個物件放進追蹤清單。
    //2.	輸家呼叫 reservation.Confirm()，記憶體裡的物件狀態變成 Confirmed。
    //3.	輸家的交易因為衝突而回滾，資料庫沒有被改。
    //4.	輸家要重新查詢這筆預約，看資料庫的真實狀態，但 EF 回給它的是追蹤清單裡的舊物件，上面寫著「Confirmed」，那是輸家自己在記憶體裡改的，不是資料庫的真相。
    //      如果這時候真實狀態其實是 Cancelled（別人剛取消），輸家卻以為是 Confirmed，就會誤判。所以回滾之後要先把追蹤清單清空，再重新查，才會真的去問資料庫。
    public void ClearTrackedChanges() => ClearTrackedChangesCount++;
}

internal sealed class FakeTransaction : IUnitOfWorkTransaction
{
    public bool Committed { get; private set; }
    public bool Disposed { get; private set; }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Committed = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;
}