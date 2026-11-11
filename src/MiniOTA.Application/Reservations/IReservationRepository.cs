using MiniOTA.Domain;

namespace MiniOTA.Application.Reservations;

// IReservationRepository.cs：預約與冪等紀錄的存取
public interface IReservationRepository
{
    Task<IdempotencyRecord?> FindIdempotencyAsync(
        string clientId, string key, CancellationToken cancellationToken);

    Task<Reservation?> GetAsync(Guid id, CancellationToken cancellationToken);

    void Add(Reservation reservation);

    void Add(IdempotencyRecord record);
}