namespace MiniOTA.Domain;

/// <summary>
/// Records the outcome of a create-reservation request (BR-018, BR-019).
/// Unique per (ClientId, Key).
/// </summary>
public sealed class IdempotencyRecord
{
    private IdempotencyRecord()
    {
    }

    public string ClientId { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;

    /// <summary>Hash of the request payload, used to detect key reuse with different data.</summary>
    public string RequestHash { get; private set; } = string.Empty;

    public Guid ReservationId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static IdempotencyRecord Create(
        string clientId,
        string key,
        string requestHash,
        Guid reservationId,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(key))
        {
            throw new DomainException("ClientId and idempotency key are required.");
        }

        return new IdempotencyRecord
        {
            ClientId = clientId,
            Key = key,
            RequestHash = requestHash,
            ReservationId = reservationId,
            CreatedAtUtc = nowUtc
        };
    }
}
