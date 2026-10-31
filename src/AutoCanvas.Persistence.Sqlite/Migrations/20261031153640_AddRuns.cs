using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoCanvas.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    automation_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    folder_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    automation_name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    automation_version = table.Column<int>(type: "INTEGER", nullable: false),
                    number = table.Column<int>(type: "INTEGER", nullable: false),
                    yaml = table.Column<string>(type: "TEXT", nullable: false),
                    state = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    trigger = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    trigger_data = table.Column<string>(type: "TEXT", nullable: true),
                    triggered_by_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    triggered_by_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    cancel_requested = table.Column<bool>(type: "INTEGER", nullable: false),
                    cancelled_by_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    finished_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_runs_automations_automation_id",
                        column: x => x.automation_id,
                        principalTable: "automations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_runs_folders_folder_id",
                        column: x => x.folder_id,
                        principalTable: "folders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "run_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    run_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    position = table.Column<int>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    activated_case = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    attempt_count = table.Column<int>(type: "INTEGER", nullable: false),
                    started_at = table.Column<long>(type: "INTEGER", nullable: true),
                    finished_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_run_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_run_jobs_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    run_job_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    number = table.Column<int>(type: "INTEGER", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    queued_at = table.Column<long>(type: "INTEGER", nullable: false),
                    not_before = table.Column<long>(type: "INTEGER", nullable: false),
                    started_at = table.Column<long>(type: "INTEGER", nullable: true),
                    finished_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_attempts_run_jobs_run_job_id",
                        column: x => x.run_job_id,
                        principalTable: "run_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_attempts_run_job_id_number",
                table: "job_attempts",
                columns: new[] { "run_job_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_attempts_status_not_before",
                table: "job_attempts",
                columns: new[] { "status", "not_before" });

            migrationBuilder.CreateIndex(
                name: "ix_run_jobs_run_id_key",
                table: "run_jobs",
                columns: new[] { "run_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_runs_automation_id_number",
                table: "runs",
                columns: new[] { "automation_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_runs_folder_id_created_at",
                table: "runs",
                columns: new[] { "folder_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_runs_state",
                table: "runs",
                column: "state");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_attempts");

            migrationBuilder.DropTable(
                name: "run_jobs");

            migrationBuilder.DropTable(
                name: "runs");
        }
    }
}
