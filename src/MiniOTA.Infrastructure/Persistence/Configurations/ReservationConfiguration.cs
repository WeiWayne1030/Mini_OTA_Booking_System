using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniOTA.Domain;

namespace MiniOTA.Infrastructure.Persistence.Configurations;

public sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("reservations", t =>
        {
            t.HasCheckConstraint("ck_reservations_room_count", "room_count > 0");
            t.HasCheckConstraint("ck_reservations_date_range", "check_out_date > check_in_date");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CheckInDate).HasColumnType("date");
        builder.Property(x => x.CheckOutDate).HasColumnType("date");

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.PaymentDeadlineUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");

        // Optimistic concurrency via PostgreSQL's system column xmin.
        builder.Property(x => x.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Ignore(x => x.Stay);

        // Supports the expiration scan (FR-006).
        builder.HasIndex(x => new { x.Status, x.PaymentDeadlineUtc })
            .HasDatabaseName("ix_reservations_status_payment_deadline");
    }
}
