using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoCanvas.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddGitHubApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "external_id",
                table: "repositories",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "installation_id",
                table: "repositories",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "github_apps",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    app_id = table.Column<long>(type: "bigint", nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    client_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    owner_login = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    html_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    web_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    api_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    private_key_ciphertext = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    webhook_secret_ciphertext = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    client_secret_ciphertext = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_github_apps", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "github_deliveries",
                columns: table => new
                {
                    delivery_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_github_deliveries", x => x.delivery_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_repositories_external_id",
                table: "repositories",
                column: "external_id");

            migrationBuilder.CreateIndex(
                name: "ix_github_deliveries_received_at",
                table: "github_deliveries",
                column: "received_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "github_apps");

            migrationBuilder.DropTable(
                name: "github_deliveries");

            migrationBuilder.DropIndex(
                name: "ix_repositories_external_id",
                table: "repositories");

            migrationBuilder.DropColumn(
                name: "external_id",
                table: "repositories");

            migrationBuilder.DropColumn(
                name: "installation_id",
                table: "repositories");
        }
    }
}
