using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoCanvas.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddJobSteps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "outputs",
                table: "run_jobs",
                type: "TEXT",
                maxLength: 65536,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancel_reason",
                table: "job_attempts",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "prepared_at",
                table: "job_attempts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "timed_out",
                table: "job_attempts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "warnings",
                table: "job_attempts",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "job_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    attempt_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    index = table.Column<int>(type: "INTEGER", nullable: false),
                    step_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    exit_code = table.Column<int>(type: "INTEGER", nullable: true),
                    outputs = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: true),
                    error = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    started_at = table.Column<long>(type: "INTEGER", nullable: true),
                    finished_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_steps_job_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "job_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_steps_attempt_id_index",
                table: "job_steps",
                columns: new[] { "attempt_id", "index" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_steps");

            migrationBuilder.DropColumn(
                name: "outputs",
                table: "run_jobs");

            migrationBuilder.DropColumn(
                name: "cancel_reason",
                table: "job_attempts");

            migrationBuilder.DropColumn(
                name: "prepared_at",
                table: "job_attempts");

            migrationBuilder.DropColumn(
                name: "timed_out",
                table: "job_attempts");

            migrationBuilder.DropColumn(
                name: "warnings",
                table: "job_attempts");
        }
    }
}
