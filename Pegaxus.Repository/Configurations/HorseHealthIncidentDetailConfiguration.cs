using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class HorseHealthIncidentDetailConfiguration : IEntityTypeConfiguration<HorseHealthIncidentDetail>
{
    public void Configure(EntityTypeBuilder<HorseHealthIncidentDetail> entity)
    {
        entity.HasKey(e => e.HorseHealthIncidentDetailId).HasName("HorseHealthIncidentDetail_pkey");

        entity.ToTable("HorseHealthIncidentDetail");

        entity.HasIndex(e => e.HorseId, "HorseHealthIncidentDetail_horse_id_idx");

        entity.HasIndex(e => new { e.IncidentReportId, e.HorseId }, "HorseHealthIncidentDetail_incident_report_id_horse_id_idx");

        entity.HasIndex(e => e.IncidentReportId, "HorseHealthIncidentDetail_incident_report_id_idx");

        entity.Property(e => e.HorseHealthIncidentDetailId).HasColumnName("horse_health_incident_detail_id");
        entity.Property(e => e.BodyTemperatureCelsius)
            .HasPrecision(4, 1)
            .HasColumnName("body_temperature_celsius");
        entity.Property(e => e.ConditionDescription).HasColumnName("condition_description");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.FirstAidProvided).HasColumnName("first_aid_provided");
        entity.Property(e => e.HeartRateBpm).HasColumnName("heart_rate_bpm");
        entity.Property(e => e.HorseId).HasColumnName("horse_id");
        entity.Property(e => e.HorseTransportable).HasColumnName("horse_transportable");
        entity.Property(e => e.IncidentReportId).HasColumnName("incident_report_id");
        entity.Property(e => e.MedicationGiven).HasColumnName("medication_given");
        entity.Property(e => e.OutcomeNote).HasColumnName("outcome_note");
        entity.Property(e => e.RespiratoryRateBpm).HasColumnName("respiratory_rate_bpm");
        entity.Property(e => e.Symptoms).HasColumnName("symptoms");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");
        entity.Property(e => e.VeterinarianContactedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("veterinarian_contacted_at");
        entity.Property(e => e.VeterinarianInstruction).HasColumnName("veterinarian_instruction");
        entity.Property(e => e.VeterinarianRequired)
            .HasDefaultValue(false)
            .HasColumnName("veterinarian_required");

        entity.HasOne(d => d.Horse).WithMany(p => p.HorseHealthIncidentDetails)
            .HasForeignKey(d => d.HorseId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("HorseHealthIncidentDetail_horse_id_fkey");

        entity.HasOne(d => d.IncidentReport).WithMany(p => p.HorseHealthIncidentDetails)
            .HasForeignKey(d => d.IncidentReportId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("HorseHealthIncidentDetail_incident_report_id_fkey");
    }
}
