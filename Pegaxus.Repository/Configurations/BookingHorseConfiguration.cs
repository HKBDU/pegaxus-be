using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class BookingHorseConfiguration : IEntityTypeConfiguration<BookingHorse>
{
    public void Configure(EntityTypeBuilder<BookingHorse> entity)
    {
        entity.HasKey(e => new { e.BookingId, e.HorseId }).HasName("BookingHorse_pkey");

        entity.ToTable("BookingHorse");

        entity.HasIndex(e => e.HorseId, "BookingHorse_horse_id_idx");

        entity.Property(e => e.BookingId).HasColumnName("booking_id");
        entity.Property(e => e.HorseId).HasColumnName("horse_id");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.SpecialHandlingNotes).HasColumnName("special_handling_notes");

        entity.HasOne(d => d.Booking).WithMany(p => p.BookingHorses)
            .HasForeignKey(d => d.BookingId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("BookingHorse_booking_id_fkey");

        entity.HasOne(d => d.Horse).WithMany(p => p.BookingHorses)
            .HasForeignKey(d => d.HorseId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("BookingHorse_horse_id_fkey");
    }
}
