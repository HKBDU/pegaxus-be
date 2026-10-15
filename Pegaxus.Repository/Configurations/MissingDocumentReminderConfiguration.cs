using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class MissingDocumentReminderConfiguration : IEntityTypeConfiguration<MissingDocumentReminder>
{
    public void Configure(EntityTypeBuilder<MissingDocumentReminder> entity)
    {
        entity.ToTable("MissingDocumentReminder");
        entity.HasKey(e => e.MissingDocumentReminderId).HasName("MissingDocumentReminder_pkey");
        entity.Property(e => e.MissingDocumentReminderId).HasColumnName("missing_document_reminder_id");
        entity.Property(e => e.ComplianceDossierId).HasColumnName("compliance_dossier_id");
        entity.Property(e => e.RecipientAccountId).HasColumnName("recipient_account_id");
        entity.Property(e => e.RequirementIds).HasColumnType("uuid[]").HasColumnName("requirement_ids");
        entity.Property(e => e.DueDate).HasColumnName("due_date");
        entity.Property(e => e.Note).HasMaxLength(2000).HasColumnName("note");
        entity.Property(e => e.AutomaticDate).HasColumnName("automatic_date");
        entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
        entity.Property(e => e.LastAttemptAt).HasColumnType("timestamp without time zone").HasColumnName("last_attempt_at");
        entity.Property(e => e.EmailSentAt).HasColumnType("timestamp without time zone").HasColumnName("email_sent_at");
        entity.Property(e => e.CancelledAt).HasColumnType("timestamp without time zone").HasColumnName("cancelled_at");
        entity.Property(e => e.EmailAttempts).HasColumnName("email_attempts");
        entity.HasIndex(e => new { e.ComplianceDossierId, e.AutomaticDate }, "MissingDocumentReminder_daily_key")
            .IsUnique().HasFilter("automatic_date IS NOT NULL");
        entity.HasIndex(e => new { e.EmailSentAt, e.CancelledAt, e.LastAttemptAt }, "MissingDocumentReminder_pending_idx");
        entity.HasIndex(e => e.RecipientAccountId, "MissingDocumentReminder_recipient_idx");
        entity.HasOne(e => e.ComplianceDossier).WithMany().HasForeignKey(e => e.ComplianceDossierId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("MissingDocumentReminder_dossier_fkey");
        entity.HasOne(e => e.RecipientAccount).WithMany().HasForeignKey(e => e.RecipientAccountId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("MissingDocumentReminder_recipient_fkey");
    }
}
