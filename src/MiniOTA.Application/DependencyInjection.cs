using Microsoft.Extensions.DependencyInjection;

namespace MiniOTA.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services)
    {
        // Register Application Services here.
        // Example:
        // services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<Availability.QueryAvailabilityHandler>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new Reservations.ReservationOptions());
        services.AddScoped<Reservations.CreateReservationHandler>();

        return services;
    }
}