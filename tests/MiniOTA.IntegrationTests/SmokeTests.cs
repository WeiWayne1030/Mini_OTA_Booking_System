using System.Net;
using System.Net.Http.Json;
using MiniOTA.Application.Availability;

namespace MiniOTA.IntegrationTests;

public class SmokeTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public SmokeTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Availability_AfterReset_ReflectsSeededInventoryFromRealDatabase()
    {
        await _factory.ResetAsync(roomsPerDay: 10, days: 30);
        var checkIn = ApiFactory.Today.AddDays(3);
        var checkOut = checkIn.AddDays(2);
        var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/availability?checkInDate={checkIn:yyyy-MM-dd}&checkOutDate={checkOut:yyyy-MM-dd}&roomCount=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<AvailabilityResult>();
        Assert.NotNull(result);
        Assert.True(result.IsAvailable);
        Assert.Equal(2, result.Days.Count);
        Assert.All(result.Days, d => Assert.Equal(10, d.Available));
    }
}