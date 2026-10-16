using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class HandoverRecordConfiguration : IEntityTypeConfiguration<HandoverRecord>
{
    public void Configure(EntityTypeBuilder<HandoverRecord> entity)
    {
        entity.HasKey(e => e.HandoverRecordId).HasName("HandoverRecord_pkey");

        entity.ToTable("HandoverRecord", tb => tb.HasComment("HandoverRecord: Một lần bàn giao trong Transport. from_type/to_type xác định bên giao/nhận là CUSTOMER, EMPLOYEE hay PROVIDER (đối tác vận chuyển); from/to_route_plan_route_id xác định chặng vừa kết thúc và chặng tiếp theo."));

        entity.HasIndex(e => e.ActualHandoverAt, "HandoverRecord_actual_handover_at_idx");

        entity.HasIndex(e => e.FromCustomerId, "HandoverRecord_from_customer_id_idx");

        entity.HasIndex(e => e.FromEmployeeId, "HandoverRecord_from_employee_id_idx");

        entity.HasIndex(e => e.FromRoutePlanRouteId, "HandoverRecord_from_route_plan_route_id_idx");

        entity.HasIndex(e => e.HandoverCode, "HandoverRecord_handover_code_key").IsUnique();

        entity.HasIndex(e => e.FromTransportProviderId, "HandoverRecord_from_transport_provider_id_idx");

        entity.HasIndex(e => e.ToTransportProviderId, "HandoverRecord_to_transport_provider_id_idx");

        entity.HasIndex(e => e.ToCustomerId, "HandoverRecord_to_customer_id_idx");

        entity.HasIndex(e => e.ToEmployeeId, "HandoverRecord_to_employee_id_idx");

        entity.HasIndex(e => e.ToRoutePlanRouteId, "HandoverRecord_to_route_plan_route_id_idx");

        entity.HasIndex(e => e.TransportId, "HandoverRecord_transport_id_idx");

        entity.HasIndex(e => new { e.TransportId, e.SequenceNo }, "HandoverRecord_transport_id_sequence_no_idx").IsUnique();

        entity.Property(e => e.HandoverRecordId).HasColumnName("handover_record_id");
        entity.Property(e => e.ActualHandoverAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("actual_handover_at");
        entity.Property(e => e.Address)
            .HasMaxLength(500)
            .HasColumnName("address");
        entity.Property(e => e.CancellationReason).HasColumnName("cancellation_reason");
        entity.Property(e => e.CancelledAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("cancelled_at");
        entity.Property(e => e.CompletedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("completed_at");
        entity.Property(e => e.Country)
            .HasMaxLength(100)
            .HasColumnName("country");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.DiscrepancyNote).HasColumnName("discrepancy_note");
        entity.Property(e => e.DocumentHandoverNote).HasColumnName("document_handover_note");
        entity.Property(e => e.EquipmentHandoverNote).HasColumnName("equipment_handover_note");
        entity.Property(e => e.FromContactName)
            .HasMaxLength(150)
            .HasColumnName("from_contact_name");
        entity.Property(e => e.FromContactPhone)
            .HasMaxLength(30)
            .HasColumnName("from_contact_phone");
        entity.Property(e => e.FromCustomerId).HasColumnName("from_customer_id");
        entity.Property(e => e.FromEmployeeId).HasColumnName("from_employee_id");
        entity.Property(e => e.FromTransportProviderId).HasColumnName("from_transport_provider_id");
        entity.Property(e => e.ToTransportProviderId).HasColumnName("to_transport_provider_id");
        entity.Property(e => e.FromPartyName)
            .HasMaxLength(200)
            .HasColumnName("from_party_name");
        entity.Property(e => e.FromRoutePlanRouteId).HasColumnName("from_route_plan_route_id");
        entity.Property(e => e.HandoverCode)
            .HasMaxLength(50)
            .HasColumnName("handover_code");
        entity.Property(e => e.HorseConditionNote).HasColumnName("horse_condition_note");
        entity.Property(e => e.LocationName)
            .HasMaxLength(200)
            .HasColumnName("location_name");
        entity.Property(e => e.PlannedHandoverAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("planned_handover_at");
        entity.Property(e => e.ReceiverSignatureUrl)
            .HasMaxLength(500)
            .HasColumnName("receiver_signature_url");
        entity.Property(e => e.SenderSignatureUrl)
            .HasMaxLength(500)
            .HasColumnName("sender_signature_url");
        entity.Property(e => e.SequenceNo).HasColumnName("sequence_no");
        entity.Property(e => e.ToContactName)
            .HasMaxLength(150)
            .HasColumnName("to_contact_name");
        entity.Property(e => e.ToContactPhone)
            .HasMaxLength(30)
            .HasColumnName("to_contact_phone");
        entity.Property(e => e.ToCustomerId).HasColumnName("to_customer_id");
        entity.Property(e => e.ToEmployeeId).HasColumnName("to_employee_id");
        entity.Property(e => e.ToPartyName)
            .HasMaxLength(200)
            .HasColumnName("to_party_name");
        entity.Property(e => e.ToRoutePlanRouteId).HasColumnName("to_route_plan_route_id");
        entity.Property(e => e.TransportId).HasColumnName("transport_id");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");

        entity.HasOne(d => d.FromCustomer).WithMany(p => p.HandoverRecordFromCustomers)
            .HasForeignKey(d => d.FromCustomerId)
            .HasConstraintName("HandoverRecord_from_customer_id_fkey");

        entity.HasOne(d => d.FromEmployee).WithMany(p => p.HandoverRecordFromEmployees)
            .HasForeignKey(d => d.FromEmployeeId)
            .HasConstraintName("HandoverRecord_from_employee_id_fkey");

        entity.HasOne(d => d.FromRoutePlanRoute).WithMany(p => p.HandoverRecordFromRoutePlanRoutes)
            .HasForeignKey(d => d.FromRoutePlanRouteId)
            .HasConstraintName("HandoverRecord_from_route_plan_route_id_fkey");

        entity.HasOne(d => d.ToCustomer).WithMany(p => p.HandoverRecordToCustomers)
            .HasForeignKey(d => d.ToCustomerId)
            .HasConstraintName("HandoverRecord_to_customer_id_fkey");

        entity.HasOne(d => d.ToEmployee).WithMany(p => p.HandoverRecordToEmployees)
            .HasForeignKey(d => d.ToEmployeeId)
            .HasConstraintName("HandoverRecord_to_employee_id_fkey");

        entity.HasOne(d => d.ToRoutePlanRoute).WithMany(p => p.HandoverRecordToRoutePlanRoutes)
            .HasForeignKey(d => d.ToRoutePlanRouteId)
            .HasConstraintName("HandoverRecord_to_route_plan_route_id_fkey");

        entity.HasOne(d => d.FromTransportProvider).WithMany(p => p.HandoverRecordFromProviders)
            .HasForeignKey(d => d.FromTransportProviderId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("HandoverRecord_from_transport_provider_id_fkey");

        entity.HasOne(d => d.ToTransportProvider).WithMany(p => p.HandoverRecordToProviders)
            .HasForeignKey(d => d.ToTransportProviderId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("HandoverRecord_to_transport_provider_id_fkey");

        entity.HasOne(d => d.Transport).WithMany(p => p.HandoverRecords)
            .HasForeignKey(d => d.TransportId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("HandoverRecord_transport_id_fkey");
    }
}
