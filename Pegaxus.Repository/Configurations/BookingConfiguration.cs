using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> entity)
    {
        entity.HasKey(e => e.BookingId).HasName("Booking_pkey");
        entity.Property(e => e.Status).IsConcurrencyToken();

        entity.ToTable("Booking");

        entity.HasIndex(e => e.AssignedEmployeeId, "Booking_assigned_employee_id_idx");

        entity.HasIndex(e => e.BookingCode, "Booking_booking_code_key").IsUnique();

        entity.HasIndex(e => e.CustomerId, "Booking_customer_id_idx");

        entity.HasIndex(e => e.RequestedDepartureDate, "Booking_requested_departure_date_idx");

        entity.Property(e => e.BookingId).HasColumnName("booking_id");
        entity.Property(e => e.ApprovedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("approved_at");
        entity.Property(e => e.AssignedEmployeeId).HasColumnName("assigned_employee_id");
        entity.Property(e => e.BookingCode)
            .HasMaxLength(50)
            .HasColumnName("booking_code");
        entity.Property(e => e.CancellationReason).HasColumnName("cancellation_reason");
        entity.Property(e => e.CancelledAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("cancelled_at");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.CustomerId).HasColumnName("customer_id");
        entity.Property(e => e.CustomerNote).HasColumnName("customer_note");
        entity.Property(e => e.DestinationAddress)
            .HasMaxLength(500)
            .HasColumnName("destination_address");
        entity.Property(e => e.DestinationCountry)
            .HasMaxLength(100)
            .HasColumnName("destination_country");
        entity.Property(e => e.OriginLatitude).HasPrecision(10, 4).HasColumnName("origin_latitude");
        entity.Property(e => e.OriginLongitude).HasPrecision(10, 4).HasColumnName("origin_longitude");
        entity.Property(e => e.DestinationLatitude).HasPrecision(10, 4).HasColumnName("destination_latitude");
        entity.Property(e => e.DestinationLongitude).HasPrecision(10, 4).HasColumnName("destination_longitude");
        entity.Property(e => e.OriginAddress)
            .HasMaxLength(500)
            .HasColumnName("origin_address");
        entity.Property(e => e.OriginCountry)
            .HasMaxLength(100)
            .HasColumnName("origin_country");
        entity.Property(e => e.RejectedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("rejected_at");
        entity.Property(e => e.RejectionReason).HasColumnName("rejection_reason");
        entity.Property(e => e.RequestedArrivalDate).HasColumnName("requested_arrival_date");
        entity.Property(e => e.RequestedDepartureDate).HasColumnName("requested_departure_date");
        entity.Property(e => e.SpecialRequirements).HasColumnName("special_requirements");
        entity.Property(e => e.SubmittedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("submitted_at");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");

        entity.HasOne(d => d.AssignedEmployee).WithMany(p => p.Bookings)
            .HasForeignKey(d => d.AssignedEmployeeId)
            .HasConstraintName("Booking_assigned_employee_id_fkey");

        entity.HasOne(d => d.Customer).WithMany(p => p.Bookings)
            .HasForeignKey(d => d.CustomerId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("Booking_customer_id_fkey");
    }
}
