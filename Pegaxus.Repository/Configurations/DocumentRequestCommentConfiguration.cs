using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class DocumentRequestCommentConfiguration : IEntityTypeConfiguration<DocumentRequestComment>
{
    public void Configure(EntityTypeBuilder<DocumentRequestComment> entity)
    {
        entity.ToTable("DocumentRequestComment");
        entity.HasKey(e => e.DocumentRequestCommentId).HasName("DocumentRequestComment_pkey");
        entity.Property(e => e.DocumentRequestCommentId).HasColumnName("document_request_comment_id");
        entity.Property(e => e.DocumentRequestId).HasColumnName("document_request_id");
        entity.Property(e => e.AuthorAccountId).HasColumnName("author_account_id");
        entity.Property(e => e.Message).HasMaxLength(2000).HasColumnName("message");
        entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
        entity.HasIndex(e => new { e.DocumentRequestId, e.CreatedAt }, "DocumentRequestComment_request_created_idx");
        entity.HasIndex(e => e.AuthorAccountId, "DocumentRequestComment_author_account_id_idx");
        entity.HasOne(e => e.DocumentRequest).WithMany().HasForeignKey(e => e.DocumentRequestId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("DocumentRequestComment_document_request_id_fkey");
        entity.HasOne(e => e.AuthorAccount).WithMany().HasForeignKey(e => e.AuthorAccountId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("DocumentRequestComment_author_account_id_fkey");
    }
}
