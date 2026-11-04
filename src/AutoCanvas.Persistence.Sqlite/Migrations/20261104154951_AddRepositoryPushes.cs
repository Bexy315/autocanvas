using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoCanvas.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddRepositoryPushes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "next_poll_at",
                table: "repositories",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "poll_minutes",
                table: "repositories",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5); // Repository.DefaultPollMinutes: connected repositories are polled, as new ones are.

            migrationBuilder.CreateTable(
                name: "repository_refs",
                columns: table => new
                {
                    repository_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    @ref = table.Column<string>(name: "ref", type: "TEXT", maxLength: 255, nullable: false),
                    commit = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repository_refs", x => new { x.repository_id, x.@ref });
                    table.ForeignKey(
                        name: "fk_repository_refs_repositories_repository_id",
                        column: x => x.repository_id,
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repository_refs");

            migrationBuilder.DropColumn(
                name: "next_poll_at",
                table: "repositories");

            migrationBuilder.DropColumn(
                name: "poll_minutes",
                table: "repositories");
        }
    }
}
