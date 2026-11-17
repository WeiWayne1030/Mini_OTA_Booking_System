using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniOTA.Api.Contracts;
using MiniOTA.Domain;
using MiniOTA.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace MiniOTA.IntegrationTests;

public class PaymentApiTests : IClassFixture<ApiFactory>
{
    private const int RoomTypeId = 1;
    private readonly ApiFactory _factory;

    public PaymentApiTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Pay_PendingReservation_Returns200_AndMovesInventoryFromReservedToConfirmed() // FR-003、BR-007
    {
        // Arrange：先建立一筆預約（第 3、4 晚，2 間）
        await _factory.ResetAsync();
        var client = _factory.CreateClient();
        var created = await CreateReservationAsync(client, "pay-1", Day(3), Day(5), rooms: 2);

        // Act：付款
        var response = await client.PostAsync($"/reservations/{created.ReservationId}/pay", content: null);

        // Assert：回應
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PayReservationResponse>();
        Assert.NotNull(body);
        Assert.Equal(created.ReservationId, body.ReservationId);
        Assert.Equal("Confirmed", body.Status);

        // Assert：庫存從 Reserved 轉成 Confirmed，Available 不變
        foreach (var night in new[] { Day(3), Day(4) })
        {
            var inventory = await InventoryAsync(night);
            Assert.Equal(8, inventory.Available);
            Assert.Equal(0, inventory.Reserved);
            Assert.Equal(2, inventory.Confirmed);
        }

        var checkOutNight = await InventoryAsync(Day(5)); // 退房日不受影響
        Assert.Equal(10, checkOutNight.Available);
        Assert.Equal(0, checkOutNight.Confirmed);

        // Assert：預約狀態與整體不變式
        var reservation = await ReservationAsync(created.ReservationId);
        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Equal(0, await InvariantViolationsAsync());
    }

    [Fact]
    public async Task Pay_UnknownReservation_Returns404_AndChangesNothing()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsync($"/reservations/{Guid.NewGuid()}/pay", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // 路由不存在也會回 404，所以要確認這個 404 是我們的業務回應，不是路由比對失敗
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Reservation not found", problem?.Title);

        Assert.Equal(0, await InvariantViolationsAsync());
    }

    [Fact]
    public async Task Pay_SameReservationTwice_Returns200BothTimes_AndMovesInventoryOnlyOnce() // BR-017
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClient();
        var created = await CreateReservationAsync(client, "pay-2", Day(3), Day(4), rooms: 2);

        var first = await client.PostAsync($"/reservations/{created.ReservationId}/pay", content: null);
        var second = await client.PostAsync($"/reservations/{created.ReservationId}/pay", content: null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var inventory = await InventoryAsync(Day(3));
        Assert.Equal(8, inventory.Available);
        Assert.Equal(0, inventory.Reserved);
        Assert.Equal(2, inventory.Confirmed); // 是 2，不是 4：第二次付款沒有再轉一次庫存
        Assert.Equal(0, await InvariantViolationsAsync());
    }

    [Fact]
    public async Task Pay_AfterPaymentDeadline_Returns409_AndLeavesInventoryReserved() // FR-003
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClient();
        var created = await CreateReservationAsync(client, "pay-3", Day(3), Day(4), rooms: 2);

        // 付款期限是 15 分鐘；把時間快轉 16 分鐘
        _factory.Clock.Advance(TimeSpan.FromMinutes(16));

        var response = await client.PostAsync($"/reservations/{created.ReservationId}/pay", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Reservation cannot be paid", problem?.Title);

        // 預約仍是待付款，庫存仍在 Reserved，沒有被轉成 Confirmed
        var reservation = await ReservationAsync(created.ReservationId);
        Assert.Equal(ReservationStatus.PendingPayment, reservation.Status);

        var inventory = await InventoryAsync(Day(3));
        Assert.Equal(2, inventory.Reserved);
        Assert.Equal(0, inventory.Confirmed);
    }

    [Fact]
    public async Task ConcurrentPayments_SoleReservationOnTheNight_AllSucceed_AndMoveInventoryOnlyOnce() // AC-007、BR-016、BR-017
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClient();
        await WarmUpAsync(client);

        var created = await CreateReservationAsync(client, "pay-race-a", Day(3), Day(4), rooms: 2);

        var results = await Task.WhenAll(
            Enumerable.Range(1, 20).Select(_ => PayAsync(client, created.ReservationId)));

        AssertAllPaymentsSucceeded(results);

        var inventory = await InventoryAsync(Day(3));
        Assert.Equal(8, inventory.Available);
        Assert.Equal(0, inventory.Reserved);
        Assert.Equal(2, inventory.Confirmed); // 只轉了一次
        Assert.Equal(ReservationStatus.Confirmed, (await ReservationAsync(created.ReservationId)).Status);
        Assert.Equal(0, await InvariantViolationsAsync());
    }

    [Fact]
    public async Task ConcurrentPayments_WhenAnotherReservationHoldsTheSameNight_AllSucceed_AndMoveInventoryOnlyOnce() // AC-007
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClient();
        await WarmUpAsync(client);


        // 同一晚的另一筆預約，讓 Reserved 夠多，庫存檢查（reserved >= 2）擋不住輸家
        await CreateReservationAsync(client, "other", Day(3), Day(4), rooms: 3);
        var target = await CreateReservationAsync(client, "pay-race-b", Day(3), Day(4), rooms: 2);

        var results = await Task.WhenAll(
            Enumerable.Range(1, 20).Select(_ => PayAsync(client, target.ReservationId)));

        AssertAllPaymentsSucceeded(results);

        var inventory = await InventoryAsync(Day(3));
        Assert.Equal(5, inventory.Available);
        Assert.Equal(3, inventory.Reserved);  // 另一筆預約的 3 間仍待付款
        Assert.Equal(2, inventory.Confirmed); // 目標預約只轉了一次（若被多轉會是 4）
        Assert.Equal(ReservationStatus.Confirmed, (await ReservationAsync(target.ReservationId)).Status);
        Assert.Equal(0, await InvariantViolationsAsync());
    }

    // 把「回應」或「測試伺服器丟回來的未處理例外」都收成結果，失敗時才能看到完整原因
    private static async Task<(HttpStatusCode? Status, string? Error)> PayAsync(HttpClient client, Guid id)
    {
        try
        {
            var response = await client.PostAsync($"/reservations/{id}/pay", content: null);
            return (response.StatusCode, null);
        }
        catch (Exception ex)
        {
            return (null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void AssertAllPaymentsSucceeded(IReadOnlyCollection<(HttpStatusCode? Status, string? Error)> results)
    {
        var failures = results.Where(r => r.Status != HttpStatusCode.OK).ToList();
        Assert.True(
            failures.Count == 0,
            $"{failures.Count} of {results.Count} payments did not return 200: " +
            string.Join(" | ", failures.Select(f => f.Error ?? f.Status.ToString()).Distinct()));
    }

    // ---------- 輔助方法（6-5 會把它們和 ReservationApiTests 的重複部分整理成共用） ----------

    private static DateOnly Day(int offsetFromToday) => ApiFactory.Today.AddDays(offsetFromToday);

    private static async Task<CreateReservationResponse> CreateReservationAsync(
        HttpClient client, string key, DateOnly checkIn, DateOnly checkOut, int rooms)
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
        request.Headers.Add("Idempotency-Key", key);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); // 前置條件必須成功

        var body = await response.Content.ReadFromJsonAsync<CreateReservationResponse>();
        Assert.NotNull(body);
        return body;
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

    private Task<Reservation> ReservationAsync(Guid id) =>
        WithDbAsync(db => db.Reservations.AsNoTracking().SingleAsync(r => r.Id == id));

    private Task<int> InvariantViolationsAsync() =>
        WithDbAsync(db => db.DailyInventories
            .CountAsync(x => x.Total != x.Available + x.Reserved + x.Confirmed));

    // 暖機：先走過一次完整的「建立 + 並發付款」，讓連線池與程式碼路徑都就緒。
    // 否則第一個請求會在其他請求還在等連線時就完成，測試等於沒有真的在比賽。
    private async Task WarmUpAsync(HttpClient client)
    {
        var warm = await CreateReservationAsync(
            client, $"warm-{Guid.NewGuid():N}", Day(10), Day(11), rooms: 1);

        await Task.WhenAll(Enumerable.Range(1, 20).Select(_ => PayAsync(client, warm.ReservationId)));
        // 結果刻意不檢查，暖機的目的只是製造負載
    }

}