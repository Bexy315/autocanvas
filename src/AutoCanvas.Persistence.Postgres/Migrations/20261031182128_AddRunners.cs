using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoCanvas.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddRunners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "runners",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    credential_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    credential_hint = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    capabilities = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    is_paused = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runners", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "runner_registration_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    hint = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    runner_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runner_registration_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_runner_registration_tokens_runners_runner_id",
                        column: x => x.runner_id,
                        principalTable: "runners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_runner_registration_tokens_runner_id",
                table: "runner_registration_tokens",
                column: "runner_id");

            migrationBuilder.CreateIndex(
                name: "ix_runner_registration_tokens_token_hash",
                table: "runner_registration_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_runners_credential_hash",
                table: "runners",
                column: "credential_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_runners_normalized_name",
                table: "runners",
                column: "normalized_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "runner_registration_tokens");

            migrationBuilder.DropTable(
                name: "runners");
        }
    }
}
