using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoCanvas.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddRepositories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "commit_sha",
                table: "runs",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ref",
                table: "runs",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "repository_id",
                table: "automations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_path",
                table: "automations",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "commit_sha",
                table: "automation_versions",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "repositories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    folder_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    normalized_name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    url = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    default_branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    resolved_branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    user_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    credential_ciphertext = table.Column<string>(type: "TEXT", nullable: true),
                    credential_key_id = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    last_synced_at = table.Column<long>(type: "INTEGER", nullable: true),
                    last_commit = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    last_error = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    problems = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    created_by_name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repositories", x => x.id);
                    table.ForeignKey(
                        name: "fk_repositories_folders_folder_id",
                        column: x => x.folder_id,
                        principalTable: "folders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_automations_repository_id",
                table: "automations",
                column: "repository_id");

            migrationBuilder.CreateIndex(
                name: "ix_repositories_folder_id_normalized_name",
                table: "repositories",
                columns: new[] { "folder_id", "normalized_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repositories");

            migrationBuilder.DropIndex(
                name: "ix_automations_repository_id",
                table: "automations");

            migrationBuilder.DropColumn(
                name: "commit_sha",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "ref",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "repository_id",
                table: "automations");

            migrationBuilder.DropColumn(
                name: "source_path",
                table: "automations");

            migrationBuilder.DropColumn(
                name: "commit_sha",
                table: "automation_versions");
        }
    }
}
