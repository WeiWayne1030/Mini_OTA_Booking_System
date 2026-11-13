using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniOTA.Api.Contracts;
using MiniOTA.Domain;
using MiniOTA.Infrastructure.Persistence;

namespace MiniOTA.IntegrationTests;

public class ReservationApiTests : IClassFixture<ApiFactory>
{
    private const int RoomTypeId = 1;
    private readonly ApiFactory _factory;

    public ReservationApiTests(ApiFactory factory) => _factory = factory;

    // ---------- 輔助方法 ----------

    private static DateOnly Day(int offsetFromToday) => ApiFactory.Today.AddDays(offsetFromToday);

    private static Task<HttpResponseMessage> PostAsync(
        HttpClient client, string? key, DateOnly checkIn, DateOnly checkOut, int rooms)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/reservations")
        {
            Content = JsonContent.Create(new
            {
                checkInDate = checkIn,
                checkOutDate = checkOut,
                roomCount = rooms
            })
        };

        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return client.SendAsync(request);
    }

    private async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await query(db);
    }

    private Task<DailyInventory> InventoryAsync(DateOnly date) =>
        WithDbAsync(db => db.DailyInventories.AsNoTracking()
            .SingleAsync(x => x.RoomTypeId == RoomTypeId && x.StayDate == date));

    private Task<int> ReservationCountAsync() =>
        WithDbAsync(db => db.Reservations.CountAsync());

    // BR-004：任何一列都必須滿足 Total = Available + Reserved + Confirmed
    private Task<int> InvariantViolationsAsync() =>
        WithDbAsync(db => db.DailyInventories
            .CountAsync(x => x.Total != x.Available + x.Reserved + x.Confirmed));

    private static Dictionary<HttpStatusCode, int> CountByStatus(IEnumerable<HttpResponseMessage> responses) =>
        responses.GroupBy(r => r.StatusCode).ToDictionary(g => g.Key, g => g.Count());

    // ---------- 基本流程 ----------

    [Fact]
    public async Task Create_WithEnoughInventory_Returns201_AndReservesEveryNightExceptCheckOut() // AC-001、BR-001
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClient();

        var response = await PostAsync(client, "k-1", Day(3), Day(5), rooms: 2); // 住第 3、4 晚

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateReservationResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.ReservationId);
        Assert.True(body.PaymentDeadlineUtc > DateTime.UtcNow);

        foreach (var night in new[] { Day(3), Day(4) })
        {
            var inventory = await InventoryAsync(night);
            Assert.Equal(8, inventory.Available);
            Assert.Equal(2, inventory.Reserved);
        }

        var checkOutNight = await InventoryAsync(Day(5)); // 退房日不佔用
        Assert.Equal(10, checkOutNight.Available);
        Assert.Equal(0, checkOutNight.Reserved);

        Assert.Equal(1, await ReservationCountAsync());
        Assert.Equal(0, await InvariantViolationsAsync());
    }

    [Fact]
    public async Task Create_WithoutIdempotencyKey_Returns400_AndChangesNothing() // BR-018
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClient();

        var response = await PostAsync(client, key: null, Day(3), Day(4), rooms: 1);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await ReservationCountAsync());
        Assert.Equal(10, (await InventoryAsync(Day(3))).Available);
    }

    // ---------- 冪等 ----------

    [Fact]
    public async Task Create_SameKeyTwice_Returns200WithSameReservation_AndReservesOnlyOnce() // AC-005
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClient();

        var first = await PostAsync(client, "k-1", Day(3), Day(4), rooms: 2);
        var second = await PostAsync(client, "k-1", Day(3), Day(4), rooms: 2);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var firstBody = await first.Content.ReadFromJsonAsync<CreateReservationResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<CreateReservationResponse>();
        Assert.Equal(firstBody!.ReservationId, secondBody!.ReservationId);

        Assert.Equal(1, await ReservationCountAsync());
        Assert.Equal(8, (await InventoryAsync(Day(3))).Available); // 只扣一次，不是兩次
    }

    [Fact]
    public async Task Create_SameKeyDifferentRequest_Returns409_AndChangesNothingMore() // BR-019
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClient();
        await PostAsync(client, "k-1", Day(3), Day(4), rooms: 2);

        var second = await PostAsync(client, "k-1", Day(3), Day(4), rooms: 5);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(1, await ReservationCountAsync());
        Assert.Equal(8, (await InventoryAsync(Day(3))).Available);
    }

    // ---------- 多晚的全有或全無 ----------

    [Fact]
    public async Task Create_WhenOneNightIsShort_Returns409_AndNoNightIsPartiallyReserved() // AC-002、AC-004、BR-010
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClient();

        // 先把第 4 晚訂到只剩 1 間
        var setup = await PostAsync(client, "setup", Day(4), Day(5), rooms: 9);
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);

        // 再訂第 3、4 兩晚各 2 間：第 3 晚夠，第 4 晚只剩 1 間
        var response = await PostAsync(client, "k-2", Day(3), Day(5), rooms: 2);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var night3 = await InventoryAsync(Day(3));
        Assert.Equal(10, night3.Available); // 第 3 晚不能被「順便」扣掉
        Assert.Equal(0, night3.Reserved);

        var night4 = await InventoryAsync(Day(4));
        Assert.Equal(1, night4.Available);
        Assert.Equal(9, night4.Reserved);

        Assert.Equal(1, await ReservationCountAsync()); // 只有 setup 那一筆
        Assert.Equal(0, await InvariantViolationsAsync());
    }

    // ---------- 並發 ----------

    [Fact]
    public async Task ConcurrentRequests_ForLastRooms_NeverOversell() // AC-003、NFR-001
    {
        await _factory.ResetAsync(roomsPerDay: 10);
        var client = _factory.CreateClient();
        var night = Day(3);

        var responses = await Task.WhenAll(
            Enumerable.Range(1, 100)
                .Select(i => PostAsync(client, $"c-{i}", night, night.AddDays(1), rooms: 1)));

        var codes = CountByStatus(responses);
        Assert.Equal(10, codes.GetValueOrDefault(HttpStatusCode.Created));
        Assert.Equal(90, codes.GetValueOrDefault(HttpStatusCode.Conflict));
        Assert.Equal(100, codes.Values.Sum()); // 沒有 500 或其他意外狀態

        var inventory = await InventoryAsync(night);
        Assert.Equal(0, inventory.Available);
        Assert.Equal(10, inventory.Reserved);
        Assert.Equal(10, await ReservationCountAsync());
        Assert.Equal(0, await InvariantViolationsAsync());
    }

    [Fact]
    public async Task ConcurrentRequests_WithSameKey_CreateExactlyOneReservation() // BR-019
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClient();

        var responses = await Task.WhenAll(
            Enumerable.Range(1, 20)
                .Select(_ => PostAsync(client, "same-key", Day(3), Day(4), rooms: 1)));

        var codes = CountByStatus(responses);
        Assert.Equal(1, codes.GetValueOrDefault(HttpStatusCode.Created));
        Assert.Equal(19, codes.GetValueOrDefault(HttpStatusCode.OK));

        // 所有人拿到的是同一筆預約
        var ids = new HashSet<Guid>();
        foreach (var response in responses)
        {
            var body = await response.Content.ReadFromJsonAsync<CreateReservationResponse>();
            Assert.NotNull(body);
            ids.Add(body.ReservationId);
        }
        Assert.Single(ids);

        // 只扣了一次庫存，失敗者的扣除已被回滾
        Assert.Equal(1, await ReservationCountAsync());
        var inventory = await InventoryAsync(Day(3));
        Assert.Equal(9, inventory.Available);
        Assert.Equal(1, inventory.Reserved);
        Assert.Equal(0, await InvariantViolationsAsync());
    }
}