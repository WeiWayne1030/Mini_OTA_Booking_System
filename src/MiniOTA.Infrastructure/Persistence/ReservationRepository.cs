using Microsoft.EntityFrameworkCore;
using MiniOTA.Application.Reservations;
using MiniOTA.Domain;

namespace MiniOTA.Infrastructure.Persistence;

public sealed class ReservationRepository : IReservationRepository
{
    private readonly ApplicationDbContext _db;

    public ReservationRepository(ApplicationDbContext db) => _db = db;

    public Task<IdempotencyRecord?> FindIdempotencyAsync(
        string clientId, string key, CancellationToken cancellationToken)
        => _db.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ClientId == clientId && x.Key == key,
                cancellationToken);

    public Task<Reservation?> GetAsync(Guid id, CancellationToken cancellationToken)
        => _db.Reservations.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public void Add(Reservation reservation) => _db.Reservations.Add(reservation); //登記未寫入

    public void Add(IdempotencyRecord record) => _db.IdempotencyRecords.Add(record); //登記未寫入
}