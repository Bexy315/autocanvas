using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoCanvas.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    occurred_at = table.Column<long>(type: "INTEGER", nullable: false),
                    actor_kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    actor_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    actor_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    target_type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    target_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    folder_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    details = table.Column<string>(type: "TEXT", nullable: true),
                    client_address = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_actor_id",
                table: "audit_events",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_folder_id",
                table: "audit_events",
                column: "folder_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_occurred_at",
                table: "audit_events",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_target_type_target_id",
                table: "audit_events",
                columns: new[] { "target_type", "target_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events");
        }
    }
}
