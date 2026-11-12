using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MiniOTA.Application.Reservations;
using Npgsql;

namespace MiniOTA.Infrastructure.Persistence;

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _db;

    public EfUnitOfWork(ApplicationDbContext db) => _db = db;

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken)
    {
        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        return new EfTransaction(transaction);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsIdempotencyKeyViolation(ex))
        {
            // 失敗的實體還留在 EF 的追蹤清單裡，不清掉會影響之後同一個 DbContext 的操作
            _db.ChangeTracker.Clear();
            throw new DuplicateIdempotencyKeyException(ex);
        }
    }

    private static bool IsIdempotencyKeyViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,   // 23505：唯一性違反
            ConstraintName: "pk_idempotency_keys"           // 只翻譯「這一個」約束
        };
}

internal sealed class EfTransaction : IUnitOfWorkTransaction
{
    private readonly IDbContextTransaction _transaction;

    public EfTransaction(IDbContextTransaction transaction) => _transaction = transaction;

    public Task CommitAsync(CancellationToken cancellationToken)
        => _transaction.CommitAsync(cancellationToken);

    // 沒有 Commit 就釋放 → EF Core 會自動回滾
    public ValueTask DisposeAsync() => _transaction.DisposeAsync();
}