namespace MiniOTA.Domain;

public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}

public sealed class InsufficientInventoryException : DomainException
{
    public InsufficientInventoryException(DateOnly stayDate, int requested, int available)
        : base($"Insufficient inventory on {stayDate:yyyy-MM-dd}: requested {requested}, available {available}.")
    {
    }
}

public sealed class InvalidReservationTransitionException : DomainException
{
    public InvalidReservationTransitionException(ReservationStatus from, ReservationStatus to)
        : base($"Reservation cannot transition from {from} to {to}.")
    {
    }
}
