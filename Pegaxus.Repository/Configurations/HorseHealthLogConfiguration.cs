using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class HorseHealthLogConfiguration : IEntityTypeConfiguration<HorseHealthLog>
{
    public void Configure(EntityTypeBuilder<HorseHealthLog> entity)
    {
        entity.HasKey(e => e.HorseHealthLogId).HasName("HorseHealthLog_pkey");

        entity.ToTable("HorseHealthLog", table => table.HasCheckConstraint("HorseHealthLog_photos_check",
            "cardinality(photo_urls) <= 5 AND array_position(photo_urls, NULL) IS NULL"));

        entity.HasIndex(e => e.CheckedAt, "HorseHealthLog_checked_at_idx");

        entity.HasIndex(e => e.EmployeeId, "HorseHealthLog_employee_id_idx");

        entity.HasIndex(e => e.HorseId, "HorseHealthLog_horse_id_idx");

        entity.HasIndex(e => new { e.TransportId, e.HorseId }, "HorseHealthLog_transport_id_horse_id_idx");

        entity.HasIndex(e => e.TransportId, "HorseHealthLog_transport_id_idx");

        entity.Property(e => e.HorseHealthLogId).HasColumnName("horse_health_log_id");
        entity.Property(e => e.BehaviorNote).HasColumnName("behavior_note");
        entity.Property(e => e.CheckedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("checked_at");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
        entity.Property(e => e.FeedingNote).HasColumnName("feeding_note");
        entity.Property(e => e.GeneralNote).HasColumnName("general_note");
        entity.Property(e => e.HeartRateBpm).HasColumnName("heart_rate_bpm");
        entity.Property(e => e.HorseId).HasColumnName("horse_id");
        entity.Property(e => e.HydrationNote).HasColumnName("hydration_note");
        entity.Property(e => e.InjuryNote).HasColumnName("injury_note");
        entity.Property(e => e.LocationDescription)
            .HasMaxLength(500)
            .HasColumnName("location_description");
        entity.Property(e => e.MedicationGiven).HasColumnName("medication_given");
        entity.Property(e => e.PhotoUrls)
            .HasColumnType("character varying(500)[]")
            .HasDefaultValueSql("ARRAY[]::character varying(500)[]")
            .HasColumnName("photo_urls");
        entity.Property(e => e.RespiratoryRateBpm).HasColumnName("respiratory_rate_bpm");
        entity.Property(e => e.SymptomNote).HasColumnName("symptom_note");
        entity.Property(e => e.TemperatureCelsius)
            .HasPrecision(4, 1)
            .HasColumnName("temperature_celsius");
        entity.Property(e => e.TransportId).HasColumnName("transport_id");
        entity.Property(e => e.TreatmentProvided).HasColumnName("treatment_provided");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");
        entity.Property(e => e.VeterinarianNote).HasColumnName("veterinarian_note");
        entity.Property(e => e.VeterinarianRequired)
            .HasDefaultValue(false)
            .HasColumnName("veterinarian_required");

        entity.HasOne(d => d.Employee).WithMany(p => p.HorseHealthLogs)
            .HasForeignKey(d => d.EmployeeId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("HorseHealthLog_employee_id_fkey");

        entity.HasOne(d => d.Horse).WithMany(p => p.HorseHealthLogs)
            .HasForeignKey(d => d.HorseId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("HorseHealthLog_horse_id_fkey");

        entity.HasOne(d => d.Transport).WithMany(p => p.HorseHealthLogs)
            .HasForeignKey(d => d.TransportId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("HorseHealthLog_transport_id_fkey");
    }
}
