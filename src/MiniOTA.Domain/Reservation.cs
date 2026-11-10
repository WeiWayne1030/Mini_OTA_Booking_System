namespace MiniOTA.Domain;

public sealed class Reservation
{
    private Reservation()
    {
    }

    public Guid Id { get; private set; }
    public int RoomTypeId { get; private set; }
    public DateOnly CheckInDate { get; private set; }
    public DateOnly CheckOutDate { get; private set; }
    public int RoomCount { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTime PaymentDeadlineUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public StayDateRange Stay => new(CheckInDate, CheckOutDate);

    public static Reservation CreatePending(
        int roomTypeId,
        StayDateRange stay,
        int roomCount,
        DateTime nowUtc,
        TimeSpan paymentTimeout)
    {
        if (roomCount <= 0)
        {
            throw new DomainException("RoomCount must be a positive integer.");
        }

        return new Reservation
        {
            Id = Guid.NewGuid(),
            RoomTypeId = roomTypeId,
            CheckInDate = stay.CheckIn,
            CheckOutDate = stay.CheckOut,
            RoomCount = roomCount,
            Status = ReservationStatus.PendingPayment,
            PaymentDeadlineUtc = nowUtc + paymentTimeout,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
    }

    /// <summary>PendingPayment -> Confirmed, only before the payment deadline.</summary>
    public void Confirm(DateTime nowUtc)
    {
        if (Status != ReservationStatus.PendingPayment || nowUtc > PaymentDeadlineUtc)
        {
            throw new InvalidReservationTransitionException(Status, ReservationStatus.Confirmed);
        }

        TransitionTo(ReservationStatus.Confirmed, nowUtc);
    }

    /// <summary>PendingPayment -> Expired (BR-020), only after the payment deadline.</summary>
    public void Expire(DateTime nowUtc)
    {
        if (Status != ReservationStatus.PendingPayment || nowUtc <= PaymentDeadlineUtc)
        {
            throw new InvalidReservationTransitionException(Status, ReservationStatus.Expired);
        }

        TransitionTo(ReservationStatus.Expired, nowUtc);
    }

    /// <summary>PendingPayment|Confirmed -> Cancelled.</summary>
    public void Cancel(DateTime nowUtc)
    {
        if (Status is not (ReservationStatus.PendingPayment or ReservationStatus.Confirmed))
        {
            throw new InvalidReservationTransitionException(Status, ReservationStatus.Cancelled);
        }

        TransitionTo(ReservationStatus.Cancelled, nowUtc);
    }

    private void TransitionTo(ReservationStatus next, DateTime nowUtc)
    {
        Status = next;
        UpdatedAtUtc = nowUtc;
    }
}
