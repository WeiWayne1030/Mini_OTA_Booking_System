namespace MiniOTA.Domain;

/// <summary>
/// Inventory of one room type for one stay date.
/// Invariant (BR-004): Total = Available + Reserved + Confirmed, all non-negative.
/// </summary>
public sealed class DailyInventory
{
    private DailyInventory()
    {
    }

    public int RoomTypeId { get; private set; }
    public DateOnly StayDate { get; private set; }
    public int Total { get; private set; }
    public int Available { get; private set; }
    public int Reserved { get; private set; }
    public int Confirmed { get; private set; }

    public static DailyInventory Create(int roomTypeId, DateOnly stayDate, int total)
    {
        if (total < 0)
        {
            throw new DomainException("Total inventory cannot be negative.");
        }

        return new DailyInventory
        {
            RoomTypeId = roomTypeId,
            StayDate = stayDate,
            Total = total,
            Available = total
        };
    }

    /// <summary>BR-006: Available -= n, Reserved += n.</summary>
    public void Reserve(int rooms)
    {
        EnsurePositive(rooms);
        if (Available < rooms)
        {
            throw new InsufficientInventoryException(StayDate, rooms, Available);
        }

        Available -= rooms;
        Reserved += rooms;
    }

    /// <summary>BR-007: Reserved -= n, Confirmed += n.</summary>
    public void Confirm(int rooms)
    {
        EnsurePositive(rooms);
        EnsureAtLeast(Reserved, rooms, nameof(Reserved));

        Reserved -= rooms;
        Confirmed += rooms;
    }

    /// <summary>BR-008: Reserved -= n, Available += n.</summary>
    public void ReleaseReserved(int rooms)
    {
        EnsurePositive(rooms);
        EnsureAtLeast(Reserved, rooms, nameof(Reserved));

        Reserved -= rooms;
        Available += rooms;
    }

    /// <summary>BR-009: Confirmed -= n, Available += n.</summary>
    public void ReleaseConfirmed(int rooms)
    {
        EnsurePositive(rooms);
        EnsureAtLeast(Confirmed, rooms, nameof(Confirmed));

        Confirmed -= rooms;
        Available += rooms;
    }

    private static void EnsurePositive(int rooms)
    {
        if (rooms <= 0)
        {
            throw new DomainException("RoomCount must be a positive integer.");
        }
    }

    private void EnsureAtLeast(int current, int rooms, string bucket)
    {
        if (current < rooms)
        {
            throw new DomainException(
                $"Inventory on {StayDate:yyyy-MM-dd} has only {current} in {bucket}, cannot move {rooms}.");
        }
    }
}
