using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoCanvas.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddInstanceSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instance_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    public_url = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    runner_url = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_by = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instance_settings", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "instance_settings");
        }
    }
}
