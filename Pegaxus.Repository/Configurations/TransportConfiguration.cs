using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class TransportConfiguration : IEntityTypeConfiguration<Transport>
{
    public void Configure(EntityTypeBuilder<Transport> entity)
    {
        entity.HasKey(e => e.TransportId).HasName("Transport_pkey");

        entity.ToTable("Transport");

        entity.HasIndex(e => e.BookingId, "Transport_booking_id_key").IsUnique();

        entity.HasIndex(e => e.TransportCode, "Transport_transport_code_key").IsUnique();

        entity.Property(e => e.TransportId).HasColumnName("transport_id");
        entity.Property(e => e.ActualArrivalAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("actual_arrival_at");
        entity.Property(e => e.ActualDepartureAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("actual_departure_at");
        entity.Property(e => e.BookingId).HasColumnName("booking_id");
        entity.Property(e => e.CancellationReason).HasColumnName("cancellation_reason");
        entity.Property(e => e.CancelledAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("cancelled_at");
        entity.Property(e => e.CompletedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("completed_at");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.TransportCode)
            .HasMaxLength(50)
            .HasColumnName("transport_code");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");

        entity.HasOne(d => d.Booking).WithOne(p => p.Transport)
            .HasForeignKey<Transport>(d => d.BookingId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("Transport_booking_id_fkey");
    }
}
