using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniOTA.Domain;

namespace MiniOTA.Infrastructure.Persistence.Configurations;

public sealed class DailyInventoryConfiguration : IEntityTypeConfiguration<DailyInventory>
{
    public void Configure(EntityTypeBuilder<DailyInventory> builder)
    {
        builder.ToTable("daily_inventory", t =>
        {
            // BR-004 / BR-005: invariants enforced by the database, not only by application code.
            t.HasCheckConstraint("ck_daily_inventory_non_negative",
                "total >= 0 AND available >= 0 AND reserved >= 0 AND confirmed >= 0");
            t.HasCheckConstraint("ck_daily_inventory_balance",
                "total = available + reserved + confirmed");
        });

        builder.HasKey(x => new { x.RoomTypeId, x.StayDate });

        builder.Property(x => x.StayDate).HasColumnType("date");
    }
}
