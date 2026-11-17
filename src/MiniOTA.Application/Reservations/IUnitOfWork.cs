namespace MiniOTA.Application.Reservations;

public interface IUnitOfWork
{
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 丟棄記憶體中追蹤的所有實體。發生並發衝突、交易回滾之後呼叫，
    /// 避免之後重新查詢時拿到過時（尚未反映資料庫真實狀態）的物件。
    /// </summary>
    void ClearTrackedChanges();
}

/// <summary>未呼叫 CommitAsync 就釋放（Dispose），代表回滾。</summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}