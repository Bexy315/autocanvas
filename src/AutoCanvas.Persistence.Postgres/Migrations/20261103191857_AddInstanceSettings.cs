using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoCanvas.Persistence.Postgres.Migrations
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
                    id = table.Column<int>(type: "integer", nullable: false),
                    public_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    runner_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
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
