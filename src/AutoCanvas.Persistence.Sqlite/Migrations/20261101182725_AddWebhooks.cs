using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoCanvas.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "automation_webhooks",
                columns: table => new
                {
                    automation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    token_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    hint = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    signing_secret = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    created_by_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_automation_webhooks", x => x.automation_id);
                    table.ForeignKey(
                        name: "fk_automation_webhooks_automations_automation_id",
                        column: x => x.automation_id,
                        principalTable: "automations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_automation_webhooks_token_hash",
                table: "automation_webhooks",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "automation_webhooks");
        }
    }
}
