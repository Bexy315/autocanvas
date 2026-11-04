using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoCanvas.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddPullRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "pull_request",
                table: "runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "repository_id",
                table: "runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "secrets_withheld",
                table: "runs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "repository_pull_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    repository_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    head_sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    head_branch = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    base_branch = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    author = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    fork = table.Column<bool>(type: "boolean", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repository_pull_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_repository_pull_requests_repositories_repository_id",
                        column: x => x.repository_id,
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_runs_repository_id",
                table: "runs",
                column: "repository_id");

            migrationBuilder.CreateIndex(
                name: "ix_repository_pull_requests_received_at",
                table: "repository_pull_requests",
                column: "received_at");

            migrationBuilder.CreateIndex(
                name: "ix_repository_pull_requests_repository_id",
                table: "repository_pull_requests",
                column: "repository_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repository_pull_requests");

            migrationBuilder.DropIndex(
                name: "ix_runs_repository_id",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "pull_request",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "repository_id",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "secrets_withheld",
                table: "runs");
        }
    }
}
