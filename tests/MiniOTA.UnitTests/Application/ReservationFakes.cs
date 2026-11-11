using MiniOTA.Application.Reservations;
using MiniOTA.Domain;

namespace MiniOTA.UnitTests.Application;

internal sealed class FakeReservationRepository : IReservationRepository
{
    public List<Reservation> Reservations { get; } = new();
    public List<IdempotencyRecord> Records { get; } = new();

    public Task<IdempotencyRecord?> FindIdempotencyAsync(
        string clientId, string key, CancellationToken cancellationToken)
        => Task.FromResult(Records.FirstOrDefault(r => r.ClientId == clientId && r.Key == key));

    public Task<Reservation?> GetAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(Reservations.FirstOrDefault(r => r.Id == id));

    public void Add(Reservation reservation) => Reservations.Add(reservation);

    public void Add(IdempotencyRecord record) => Records.Add(record);
}

internal sealed class FakeInventoryReserver : IInventoryReserver
{
    /// <summary>設成 false 就模擬「庫存不足」。</summary>
    public bool Result { get; set; } = true;

    public int CallCount { get; private set; }

    public (int RoomTypeId, StayDateRange Stay, int Rooms)? LastCall { get; private set; }

    public Task<bool> TryReserveAsync(
        int roomTypeId, StayDateRange stay, int rooms, CancellationToken cancellationToken)
    {
        CallCount++;
        LastCall = (roomTypeId, stay, rooms);
        return Task.FromResult(Result);
    }
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public List<FakeTransaction> Transactions { get; } = new();

    public int SaveCount { get; private set; }

    /// <summary>設成 true 就模擬「寫入資料庫時失敗」。</summary>
    public bool ThrowOnSave { get; set; }

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        var transaction = new FakeTransaction();
        Transactions.Add(transaction);
        return Task.FromResult<IUnitOfWorkTransaction>(transaction);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        if (ThrowOnSave)
        {
            throw new InvalidOperationException("Simulated database failure.");
        }

        return Task.CompletedTask;
    }
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