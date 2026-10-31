using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoCanvas.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddJobQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "error",
                table: "job_attempts",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "labels",
                table: "job_attempts",
                type: "TEXT",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "lease_expires_at",
                table: "job_attempts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "needs_container",
                table: "job_attempts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "node_types",
                table: "job_attempts",
                type: "TEXT",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "runner_id",
                table: "job_attempts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "timeout_minutes",
                table: "job_attempts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.CreateIndex(
                name: "ix_job_attempts_runner_id_status",
                table: "job_attempts",
                columns: new[] { "runner_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_job_attempts_status_lease_expires_at",
                table: "job_attempts",
                columns: new[] { "status", "lease_expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_job_attempts_runner_id_status",
                table: "job_attempts");

            migrationBuilder.DropIndex(
                name: "ix_job_attempts_status_lease_expires_at",
                table: "job_attempts");

            migrationBuilder.DropColumn(
                name: "error",
                table: "job_attempts");

            migrationBuilder.DropColumn(
                name: "labels",
                table: "job_attempts");

            migrationBuilder.DropColumn(
                name: "lease_expires_at",
                table: "job_attempts");

            migrationBuilder.DropColumn(
                name: "needs_container",
                table: "job_attempts");

            migrationBuilder.DropColumn(
                name: "node_types",
                table: "job_attempts");

            migrationBuilder.DropColumn(
                name: "runner_id",
                table: "job_attempts");

            migrationBuilder.DropColumn(
                name: "timeout_minutes",
                table: "job_attempts");
        }
    }
}
