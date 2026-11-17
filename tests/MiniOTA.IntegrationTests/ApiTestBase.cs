using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniOTA.Api.Contracts;
using MiniOTA.Domain;
using MiniOTA.Infrastructure.Persistence;

namespace MiniOTA.IntegrationTests;

/// <summary>整合測試共用的輔助方法。繼承它的測試類別會自動共用同一個 ApiFactory。</summary>
/// abstract： 這個類別只能被繼承，不能單獨建立，xUnit 也不會把它當成測試類別來跑（它沒有 [Fact]）。
/// CreateReservationAsync 用PostAsync 組出來共用
/// protected： 只有它自己和子類別看得到，外部看不到。
public abstract class ApiTestBase : IClassFixture<ApiFactory>
{
    protected const int RoomTypeId = 1;

    protected readonly ApiFactory _factory;

    protected ApiTestBase(ApiFactory factory) => _factory = factory;

    // ---------- 日期 ----------

    protected static DateOnly Day(int offsetFromToday) => ApiFactory.Today.AddDays(offsetFromToday);

    // ---------- HTTP ----------

    /// <summary>送出建立預約的請求，回傳原始回應（不檢查狀態碼）。key 為 null 代表不帶 Idempotency-Key。</summary>
    protected static Task<HttpResponseMessage> PostAsync(
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

    /// <summary>建立預約並要求成功（201）。用於「準備情境」，失敗代表前置條件壞了。</summary>
    protected static async Task<CreateReservationResponse> CreateReservationAsync(
        HttpClient client, string key, DateOnly checkIn, DateOnly checkOut, int rooms)
    {
        var response = await PostAsync(client, key, checkIn, checkOut, rooms);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreateReservationResponse>();
        Assert.NotNull(body);
        return body;
    }

    protected static Dictionary<HttpStatusCode, int> CountByStatus(IEnumerable<HttpResponseMessage> responses) =>
        responses.GroupBy(r => r.StatusCode).ToDictionary(g => g.Key, g => g.Count());

    // ---------- 直接查資料庫 ----------

    protected async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await query(db);
    }

    protected Task<DailyInventory> InventoryAsync(DateOnly date) =>
        WithDbAsync(db => db.DailyInventories.AsNoTracking()
            .SingleAsync(x => x.RoomTypeId == RoomTypeId && x.StayDate == date));

    protected Task<Reservation> ReservationAsync(Guid id) =>
        WithDbAsync(db => db.Reservations.AsNoTracking().SingleAsync(r => r.Id == id));

    protected Task<int> ReservationCountAsync() =>
        WithDbAsync(db => db.Reservations.CountAsync());

    // BR-004：任何一列都必須滿足 Total = Available + Reserved + Confirmed
    protected Task<int> InvariantViolationsAsync() =>
        WithDbAsync(db => db.DailyInventories
            .CountAsync(x => x.Total != x.Available + x.Reserved + x.Confirmed));
}