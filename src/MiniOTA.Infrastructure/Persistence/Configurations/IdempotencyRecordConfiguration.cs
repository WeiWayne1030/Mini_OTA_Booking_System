using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniOTA.Domain;

namespace MiniOTA.Infrastructure.Persistence.Configurations;

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_keys");

        // BR-018: uniqueness is enforced by the database primary key.
        builder.HasKey(x => new { x.ClientId, x.Key });

        builder.Property(x => x.ClientId).HasMaxLength(100);
        builder.Property(x => x.Key).HasMaxLength(100);
        builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");

        builder.HasOne<Reservation>()
            .WithMany()
            .HasForeignKey(x => x.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
