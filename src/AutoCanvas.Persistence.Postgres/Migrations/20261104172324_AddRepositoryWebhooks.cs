using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoCanvas.Persistence.Postgres.Migrations
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    repository_id = table.Column<Guid>(type: "uuid", nullable: false),
                    @ref = table.Column<string>(name: "ref", type: "character varying(255)", maxLength: 255, nullable: false),
                    before = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    after = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    changed_files = table.Column<string>(type: "text", nullable: true),
                    pusher = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                    repository_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hint = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    secret_ciphertext = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
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
