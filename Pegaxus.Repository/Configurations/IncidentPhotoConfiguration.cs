using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class IncidentPhotoConfiguration : IEntityTypeConfiguration<IncidentPhoto>
{
    public void Configure(EntityTypeBuilder<IncidentPhoto> entity)
    {
        entity.HasKey(e => e.IncidentPhotoId).HasName("IncidentPhoto_pkey");

        entity.ToTable("IncidentPhoto");

        entity.HasIndex(e => e.IncidentReportId, "IncidentPhoto_incident_report_id_idx");
        entity.HasIndex(e => e.VehicleIncidentDetailId, "IncidentPhoto_vehicle_incident_detail_id_idx");
        entity.HasIndex(e => e.HorseHealthIncidentDetailId, "IncidentPhoto_horse_health_incident_detail_id_idx");
        entity.HasIndex(e => e.EmergencyCostRequestId, "IncidentPhoto_emergency_cost_request_id_idx");

        entity.Property(e => e.IncidentPhotoId).HasColumnName("incident_photo_id");
        entity.Property(e => e.IncidentReportId).HasColumnName("incident_report_id");
        entity.Property(e => e.VehicleIncidentDetailId).HasColumnName("vehicle_incident_detail_id");
        entity.Property(e => e.HorseHealthIncidentDetailId).HasColumnName("horse_health_incident_detail_id");
        entity.Property(e => e.EmergencyCostRequestId).HasColumnName("emergency_cost_request_id");
        entity.Property(e => e.PhotoUrl).HasMaxLength(500).HasColumnName("photo_url");
        entity.Property(e => e.Caption).HasMaxLength(500).HasColumnName("caption");
        entity.Property(e => e.SortOrder).HasDefaultValue(0).HasColumnName("sort_order");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");

        entity.HasOne(e => e.IncidentReport).WithMany(e => e.IncidentPhotos)
            .HasForeignKey(e => e.IncidentReportId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("IncidentPhoto_incident_report_id_fkey");

        entity.HasOne(e => e.VehicleIncidentDetail).WithMany(e => e.IncidentPhotos)
            .HasForeignKey(e => e.VehicleIncidentDetailId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("IncidentPhoto_vehicle_incident_detail_id_fkey");

        entity.HasOne(e => e.HorseHealthIncidentDetail).WithMany(e => e.IncidentPhotos)
            .HasForeignKey(e => e.HorseHealthIncidentDetailId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("IncidentPhoto_horse_health_incident_detail_id_fkey");

        entity.HasOne(e => e.EmergencyCostRequest).WithMany(e => e.IncidentPhotos)
            .HasForeignKey(e => e.EmergencyCostRequestId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("IncidentPhoto_emergency_cost_request_id_fkey");
    }
}
