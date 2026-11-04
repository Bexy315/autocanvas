using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoCanvas.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddRepositoryWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "repository_pushes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    repository_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    @ref = table.Column<string>(name: "ref", type: "TEXT", maxLength: 255, nullable: false),
                    before = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    after = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    changed_files = table.Column<string>(type: "TEXT", nullable: true),
                    pusher = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    received_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repository_pushes", x => x.id);
                    table.ForeignKey(
                        name: "fk_repository_pushes_repositories_repository_id",
                        column: x => x.repository_id,
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "repository_webhooks",
                columns: table => new
                {
                    repository_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    token_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    hint = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    secret_ciphertext = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    created_by_name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repository_webhooks", x => x.repository_id);
                    table.ForeignKey(
                        name: "fk_repository_webhooks_repositories_repository_id",
                        column: x => x.repository_id,
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_repository_pushes_received_at",
                table: "repository_pushes",
                column: "received_at");

            migrationBuilder.CreateIndex(
                name: "ix_repository_pushes_repository_id",
                table: "repository_pushes",
                column: "repository_id");

            migrationBuilder.CreateIndex(
                name: "ix_repository_webhooks_token_hash",
                table: "repository_webhooks",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repository_pushes");

            migrationBuilder.DropTable(
                name: "repository_webhooks");
        }
    }
}
