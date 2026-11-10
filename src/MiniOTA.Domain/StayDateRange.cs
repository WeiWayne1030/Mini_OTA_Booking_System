namespace MiniOTA.Domain;

/// <summary>
/// Stay dates from check-in (inclusive) to check-out (exclusive). See BR-001, BR-002.
/// </summary>
public readonly record struct StayDateRange
{
    public DateOnly CheckIn { get; }
    public DateOnly CheckOut { get; }

    public StayDateRange(DateOnly checkIn, DateOnly checkOut)
    {
        if (checkOut <= checkIn)
        {
            throw new DomainException("CheckOutDate must be later than CheckInDate.");
        }

        CheckIn = checkIn;
        CheckOut = checkOut;
    }

    public int Nights => CheckOut.DayNumber - CheckIn.DayNumber;

    public IEnumerable<DateOnly> StayDates()
    {
        for (var date = CheckIn; date < CheckOut; date = date.AddDays(1))
        {
            yield return date;
        }
    }
}
