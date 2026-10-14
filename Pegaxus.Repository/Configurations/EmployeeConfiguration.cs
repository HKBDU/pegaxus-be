using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository.Configurations;

public sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> entity)
    {
        entity.HasKey(e => e.EmployeeId).HasName("Employee_pkey");

        entity.ToTable("Employee");

        entity.HasIndex(e => e.AccountId, "Employee_account_id_key").IsUnique();

        entity.HasIndex(e => e.EmployeeCode, "Employee_employee_code_key").IsUnique();

        entity.HasIndex(e => e.IsActive, "Employee_is_active_idx");

        entity.HasIndex(e => e.IsDeleted, "Employee_is_deleted_idx");

        entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
        entity.Property(e => e.AccountId).HasColumnName("account_id");
        entity.Property(e => e.Address)
            .HasMaxLength(500)
            .HasColumnName("address");
        entity.Property(e => e.Country)
            .HasMaxLength(100)
            .HasColumnName("country");
        entity.Property(e => e.DateOfBirth)
            .HasColumnName("date_of_birth");
        entity.Property(e => e.CreatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("created_at");
        entity.Property(e => e.DeletedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("deleted_at");
        entity.Property(e => e.DeletedByAccountId).HasColumnName("deleted_by_account_id");
        entity.Property(e => e.EmployeeCode)
            .HasMaxLength(50)
            .HasColumnName("employee_code");
        entity.Property(e => e.FullName)
            .HasMaxLength(150)
            .HasColumnName("full_name");
        entity.Property(e => e.IsActive)
            .HasDefaultValue(true)
            .HasColumnName("is_active");
        entity.Property(e => e.IsDeleted)
            .HasDefaultValue(false)
            .HasColumnName("is_deleted");
        entity.Property(e => e.Phone)
            .HasMaxLength(30)
            .HasColumnName("phone");
        entity.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp without time zone")
            .HasColumnName("updated_at");

        entity.HasOne(d => d.Account).WithOne(p => p.EmployeeAccount)
            .HasForeignKey<Employee>(d => d.AccountId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("Employee_account_id_fkey");

        entity.HasOne(d => d.DeletedByAccount).WithMany(p => p.EmployeeDeletedByAccounts)
            .HasForeignKey(d => d.DeletedByAccountId)
            .HasConstraintName("Employee_deleted_by_account_id_fkey");
    }
}
