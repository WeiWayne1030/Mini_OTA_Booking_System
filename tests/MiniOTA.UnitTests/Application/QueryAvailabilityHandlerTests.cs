using MiniOTA.Application.Availability;
using MiniOTA.Domain;

namespace MiniOTA.UnitTests.Application;

public class QueryAvailabilityHandlerTests
{
    private static readonly DateOnly Nov10 = new(2026, 11, 10);
    private static readonly DateOnly Nov11 = new(2026, 11, 11);
    private static readonly DateOnly Nov12 = new(2026, 11, 12);
    private static readonly DateOnly Nov13 = new(2026, 11, 13);

    // 建立某天「剩下 available 間」的庫存（總共 total 間）
    private static DailyInventory Day(DateOnly date, int available, int total = 10, int roomTypeId = 1)
    {
        var inventory = DailyInventory.Create(roomTypeId, date, total);
        if (total - available > 0)
        {
            inventory.Reserve(total - available);
        }

        return inventory;
    }

    private static AvailabilityQuery Query(DateOnly checkIn, DateOnly checkOut, int rooms) =>
        new(RoomTypeId: 1, checkIn, checkOut, rooms);

    private static Task<AvailabilityResult> Run(FakeInventoryReader reader, AvailabilityQuery query) =>
        new QueryAvailabilityHandler(reader).HandleAsync(query, CancellationToken.None);

    [Fact]
    public async Task AllNightsHaveEnoughRooms_IsAvailable()
    {
        var reader = new FakeInventoryReader(Day(Nov10, 10), Day(Nov11, 10), Day(Nov12, 10));

        var result = await Run(reader, Query(Nov10, Nov13, rooms: 2));

        Assert.True(result.IsAvailable);
        Assert.Equal(3, result.Days.Count);
        Assert.All(result.Days, d => Assert.Equal(10, d.Available));
    }

    [Fact]
    public async Task OneNightShort_IsNotAvailable_ButStillListsEveryNight() // FR-001、AC-004
    {
        var reader = new FakeInventoryReader(Day(Nov10, 10), Day(Nov11, 1), Day(Nov12, 10));

        var result = await Run(reader, Query(Nov10, Nov13, rooms: 2));

        Assert.False(result.IsAvailable);
        Assert.Equal(3, result.Days.Count);
        Assert.Equal(1, result.Days.Single(d => d.StayDate == Nov11).Available);
    }

    [Fact]
    public async Task ExactlyEnoughRooms_IsAvailable()
    {
        var reader = new FakeInventoryReader(Day(Nov10, 2));

        var result = await Run(reader, Query(Nov10, Nov11, rooms: 2));

        Assert.True(result.IsAvailable);
    }

    [Fact]
    public async Task MissingInventoryRow_IsTreatedAsZeroAvailable()
    {
        // 11/11 沒有任何庫存資料
        var reader = new FakeInventoryReader(Day(Nov10, 10), Day(Nov12, 10));

        var result = await Run(reader, Query(Nov10, Nov13, rooms: 1));

        Assert.False(result.IsAvailable);
        Assert.Equal(0, result.Days.Single(d => d.StayDate == Nov11).Available);
    }

    [Fact]
    public async Task CheckOutNight_DoesNotAffectResult() // BR-001
    {
        // 退房日 11/12 已經客滿，但不應影響 11/10～11/11 的住宿
        var reader = new FakeInventoryReader(Day(Nov10, 5), Day(Nov11, 5), Day(Nov12, 0));

        var result = await Run(reader, Query(Nov10, Nov12, rooms: 1));

        Assert.True(result.IsAvailable);
        Assert.Equal(2, result.Days.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositiveRoomCount_ThrowsWithoutQueryingInventory(int rooms) // BR-003
    {
        var reader = new FakeInventoryReader(Day(Nov10, 10));

        await Assert.ThrowsAsync<DomainException>(
            () => Run(reader, Query(Nov10, Nov11, rooms)));

        Assert.Equal(0, reader.CallCount);
    }

    [Fact]
    public async Task CheckOutNotLaterThanCheckIn_Throws() // BR-002
    {
        var reader = new FakeInventoryReader(Day(Nov10, 10));

        await Assert.ThrowsAsync<DomainException>(
            () => Run(reader, Query(Nov11, Nov10, rooms: 1)));
    }

    // 假的庫存讀取器：資料放在記憶體，行為與真正的 InventoryReader 一致（含退房日不包含）
    private sealed class FakeInventoryReader : IInventoryReader
    {
        private readonly List<DailyInventory> _rows;

        public FakeInventoryReader(params DailyInventory[] rows) => _rows = rows.ToList();

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<DailyInventory>> GetRangeAsync(
            int roomTypeId, StayDateRange stay, CancellationToken cancellationToken)
        {
            CallCount++;

            IReadOnlyList<DailyInventory> result = _rows
                .Where(r => r.RoomTypeId == roomTypeId
                            && r.StayDate >= stay.CheckIn
                            && r.StayDate < stay.CheckOut)
                .ToList();

            return Task.FromResult(result);
        }
    }
}