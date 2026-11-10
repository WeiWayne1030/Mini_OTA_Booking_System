using MiniOTA.Domain;

namespace MiniOTA.UnitTests.Domain;

public class StayDateRangeTests
{
    [Fact]
    public void Constructor_ValidRange_ExposesDatesAndNights()
    {
        var range = new StayDateRange(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 13));

        Assert.Equal(new DateOnly(2026, 11, 10), range.CheckIn);
        Assert.Equal(new DateOnly(2026, 11, 13), range.CheckOut);
        Assert.Equal(3, range.Nights);
    }

    [Fact]
    public void StayDates_IncludesCheckInButExcludesCheckOut() // BR-001
    {
        var range = new StayDateRange(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 13));

        var dates = range.StayDates().ToList();

        Assert.Equal(
            new[]
            {
                new DateOnly(2026, 11, 10),
                new DateOnly(2026, 11, 11),
                new DateOnly(2026, 11, 12)
            },
            dates);
    }

    [Fact]
    public void StayDates_CrossingMonthAndYearBoundary_ListsEveryNight()
    {
        var range = new StayDateRange(new DateOnly(2026, 12, 30), new DateOnly(2027, 1, 2));

        var dates = range.StayDates().ToList();

        Assert.Equal(
            new[]
            {
                new DateOnly(2026, 12, 30),
                new DateOnly(2026, 12, 31),
                new DateOnly(2027, 1, 1)
            },
            dates);
    }

    [Theory]
    [InlineData(2026, 11, 10, 2026, 11, 10)] // 同一天
    [InlineData(2026, 11, 13, 2026, 11, 10)] // 退房日早於入住日
    public void Constructor_CheckOutNotLaterThanCheckIn_Throws(
        int inYear, int inMonth, int inDay, int outYear, int outMonth, int outDay) // BR-002
    {
        var checkIn = new DateOnly(inYear, inMonth, inDay);
        var checkOut = new DateOnly(outYear, outMonth, outDay);

        Assert.Throws<DomainException>(() => new StayDateRange(checkIn, checkOut));
    }
}