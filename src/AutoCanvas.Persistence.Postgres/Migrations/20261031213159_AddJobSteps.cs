using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoCanvas.Persistence.Postgres.Migrations
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
                type: "character varying(65536)",
                maxLength: 65536,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancel_reason",
                table: "job_attempts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "prepared_at",
                table: "job_attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "timed_out",
                table: "job_attempts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "warnings",
                table: "job_attempts",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "job_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    index = table.Column<int>(type: "integer", nullable: false),
                    step_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    exit_code = table.Column<int>(type: "integer", nullable: true),
                    outputs = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: true),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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
