using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoCanvas.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "completion_handled",
                table: "runs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "automation_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    automation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    position = table.Column<int>(type: "INTEGER", nullable: false),
                    cron = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    time_zone = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    interval_seconds = table.Column<long>(type: "INTEGER", nullable: true),
                    next_at = table.Column<long>(type: "INTEGER", nullable: false),
                    last_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_automation_schedules", x => x.id);
                    table.ForeignKey(
                        name: "fk_automation_schedules_automations_automation_id",
                        column: x => x.automation_id,
                        principalTable: "automations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "completion_triggers",
                columns: table => new
                {
                    automation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    target_folder_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    target_name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    on = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_completion_triggers", x => x.automation_id);
                    table.ForeignKey(
                        name: "fk_completion_triggers_automations_automation_id",
                        column: x => x.automation_id,
                        principalTable: "automations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_runs_completion_handled_state",
                table: "runs",
                columns: new[] { "completion_handled", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_automation_schedules_automation_id",
                table: "automation_schedules",
                column: "automation_id");

            migrationBuilder.CreateIndex(
                name: "ix_automation_schedules_next_at",
                table: "automation_schedules",
                column: "next_at");

            migrationBuilder.CreateIndex(
                name: "ix_completion_triggers_target_folder_id_target_name",
                table: "completion_triggers",
                columns: new[] { "target_folder_id", "target_name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "automation_schedules");

            migrationBuilder.DropTable(
                name: "completion_triggers");

            migrationBuilder.DropIndex(
                name: "ix_runs_completion_handled_state",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "completion_handled",
                table: "runs");
        }
    }
}
