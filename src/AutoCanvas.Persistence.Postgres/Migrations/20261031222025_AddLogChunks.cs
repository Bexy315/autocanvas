using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoCanvas.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddLogChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "log_bytes",
                table: "job_attempts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "log_truncated",
                table: "job_attempts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "log_chunks",
                columns: table => new
                {
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    step_index = table.Column<int>(type: "integer", nullable: true),
                    lines = table.Column<string>(type: "character varying(1048576)", maxLength: 1048576, nullable: false),
                    line_count = table.Column<int>(type: "integer", nullable: false),
                    byte_count = table.Column<int>(type: "integer", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_log_chunks", x => new { x.attempt_id, x.sequence });
                    table.ForeignKey(
                        name: "fk_log_chunks_job_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "job_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "log_chunks");

            migrationBuilder.DropColumn(
                name: "log_bytes",
                table: "job_attempts");

            migrationBuilder.DropColumn(
                name: "log_truncated",
                table: "job_attempts");
        }
    }
}
