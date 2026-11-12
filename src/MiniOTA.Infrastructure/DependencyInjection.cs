using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniOTA.Application.Availability;
using MiniOTA.Application.Reservations;
using MiniOTA.Infrastructure.Persistence;

namespace MiniOTA.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is not configured.");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString)
                   .UseSnakeCaseNamingConvention());

        services.AddScoped<IInventoryReader, InventoryReader>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IInventoryReserver, InventoryReserver>();

        return services;
    }
}
