using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniOTA.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MiniOTA.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public ApiFactory()
    {
        // 連線字串放在測試專案的 User Secrets（也允許用環境變數覆寫，之後 CI 可用）
        var config = new ConfigurationBuilder()
            .AddUserSecrets<ApiFactory>()
            .AddEnvironmentVariables()
            .Build();

        _connectionString = config.GetConnectionString("TestDb")
            ?? throw new InvalidOperationException(
                "Connection string 'TestDb' is not configured. " +
                "Run: dotnet user-secrets set \"ConnectionStrings:TestDb\" \"...\" " +
                "--project tests/MiniOTA.IntegrationTests");
    }

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    public AdjustableTimeProvider Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // 用 Testing 環境：Program.cs 只在 Development 才會塞種子資料與開 Swagger
        builder.UseEnvironment("Testing");
        // 用可調整的時鐘取代正式的 TimeProvider.System，讓測試可以快轉時間
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });

        // 讓 API 連到測試資料庫，而不是開發用的資料庫
        builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
    }

    /// <summary>套用 migration、清空所有資料表，並重新建立「今天起 days 天」的庫存。</summary>
    public async Task ResetAsync(int roomsPerDay = 10, int days = 30)
    {
        Clock.Reset(); //測試開始時時鐘歸零
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE idempotency_keys, reservations, daily_inventory");

        // 重用開發用的種子程式，不重寫一份
        await InventorySeeder.SeedAsync(db, Today, totalRooms: roomsPerDay, days: days);
    }
}