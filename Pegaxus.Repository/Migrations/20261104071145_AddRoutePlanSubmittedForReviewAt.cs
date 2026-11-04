using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pegaxus.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddRoutePlanSubmittedForReviewAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "submitted_for_review_at",
                table: "RoutePlan",
                type: "timestamp without time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "submitted_for_review_at",
                table: "RoutePlan");
        }
    }
}
