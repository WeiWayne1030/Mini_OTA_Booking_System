using MiniOTA.Domain;

namespace MiniOTA.UnitTests.Domain;

public class DailyInventoryTests
{
    private static readonly DateOnly Date = new(2026, 12, 1);

    private static DailyInventory NewInventory(int total = 10)
        => DailyInventory.Create(roomTypeId: 1, Date, total);

    // BR-004：任何時候 Total = Available + Reserved + Confirmed，且都不為負
    private static void AssertInvariant(DailyInventory i)
    {
        Assert.Equal(i.Total, i.Available + i.Reserved + i.Confirmed);
        Assert.True(i.Available >= 0);
        Assert.True(i.Reserved >= 0);
        Assert.True(i.Confirmed >= 0);
    }

    [Fact]
    public void Create_StartsWithAllRoomsAvailable()
    {
        var inventory = NewInventory(10);

        Assert.Equal(10, inventory.Total);
        Assert.Equal(10, inventory.Available);
        Assert.Equal(0, inventory.Reserved);
        Assert.Equal(0, inventory.Confirmed);
        AssertInvariant(inventory);
    }

    [Fact]
    public void Reserve_MovesRoomsFromAvailableToReserved() // BR-006
    {
        var inventory = NewInventory(10);

        inventory.Reserve(3);

        Assert.Equal(7, inventory.Available);
        Assert.Equal(3, inventory.Reserved);
        AssertInvariant(inventory);
    }

    [Fact]
    public void Reserve_MoreThanAvailable_ThrowsAndLeavesInventoryUnchanged() // BR-005
    {
        var inventory = NewInventory(10);
        inventory.Reserve(8);

        Assert.Throws<InsufficientInventoryException>(() => inventory.Reserve(3));

        Assert.Equal(2, inventory.Available);
        Assert.Equal(8, inventory.Reserved);
        AssertInvariant(inventory);
    }

    [Fact]
    public void Confirm_MovesRoomsFromReservedToConfirmed() // BR-007
    {
        var inventory = NewInventory(10);
        inventory.Reserve(4);

        inventory.Confirm(4);

        Assert.Equal(0, inventory.Reserved);
        Assert.Equal(4, inventory.Confirmed);
        Assert.Equal(6, inventory.Available);
        AssertInvariant(inventory);
    }

    [Fact]
    public void ReleaseReserved_ReturnsRoomsToAvailable() // BR-008
    {
        var inventory = NewInventory(10);
        inventory.Reserve(4);

        inventory.ReleaseReserved(4);

        Assert.Equal(10, inventory.Available);
        Assert.Equal(0, inventory.Reserved);
        AssertInvariant(inventory);
    }

    [Fact]
    public void ReleaseConfirmed_ReturnsRoomsToAvailable() // BR-009
    {
        var inventory = NewInventory(10);
        inventory.Reserve(4);
        inventory.Confirm(4);

        inventory.ReleaseConfirmed(4);

        Assert.Equal(10, inventory.Available);
        Assert.Equal(0, inventory.Confirmed);
        AssertInvariant(inventory);
    }

    [Fact]
    public void Confirm_MoreThanReserved_Throws()
    {
        var inventory = NewInventory(10);
        inventory.Reserve(2);

        Assert.Throws<DomainException>(() => inventory.Confirm(3));

        AssertInvariant(inventory);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Reserve_NonPositiveRooms_Throws(int rooms) // BR-003
    {
        var inventory = NewInventory(10);

        Assert.Throws<DomainException>(() => inventory.Reserve(rooms));
    }
}