using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoCanvas.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    automation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    warnings = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    truncated = table.Column<bool>(type: "boolean", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tests_total = table.Column<int>(type: "integer", nullable: true),
                    tests_passed = table.Column<int>(type: "integer", nullable: true),
                    tests_failed = table.Column<int>(type: "integer", nullable: true),
                    tests_skipped = table.Column<int>(type: "integer", nullable: true),
                    tests_duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    lines_covered = table.Column<int>(type: "integer", nullable: true),
                    lines_valid = table.Column<int>(type: "integer", nullable: true),
                    branches_covered = table.Column<int>(type: "integer", nullable: true),
                    branches_valid = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_reports_job_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "job_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_coverage_classes",
                columns: table => new
                {
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    package_ordinal = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    file = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    lines_covered = table.Column<int>(type: "integer", nullable: false),
                    lines_valid = table.Column<int>(type: "integer", nullable: false),
                    branches_covered = table.Column<int>(type: "integer", nullable: false),
                    branches_valid = table.Column<int>(type: "integer", nullable: false),
                    uncovered = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_coverage_classes", x => new { x.report_id, x.ordinal });
                    table.ForeignKey(
                        name: "fk_report_coverage_classes_reports_report_id",
                        column: x => x.report_id,
                        principalTable: "reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_coverage_packages",
                columns: table => new
                {
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    lines_covered = table.Column<int>(type: "integer", nullable: false),
                    lines_valid = table.Column<int>(type: "integer", nullable: false),
                    branches_covered = table.Column<int>(type: "integer", nullable: false),
                    branches_valid = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_coverage_packages", x => new { x.report_id, x.ordinal });
                    table.ForeignKey(
                        name: "fk_report_coverage_packages_reports_report_id",
                        column: x => x.report_id,
                        principalTable: "reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_test_cases",
                columns: table => new
                {
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    group = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    suite = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    name = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    full_name = table.Column<string>(type: "character varying(2001)", maxLength: 2001, nullable: false),
                    name_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    stack_trace = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    output = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_test_cases", x => new { x.report_id, x.ordinal });
                    table.ForeignKey(
                        name: "fk_report_test_cases_reports_report_id",
                        column: x => x.report_id,
                        principalTable: "reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_report_test_cases_name_hash",
                table: "report_test_cases",
                column: "name_hash");

            migrationBuilder.CreateIndex(
                name: "ix_reports_attempt_id_name_path",
                table: "reports",
                columns: new[] { "attempt_id", "name", "path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reports_run_id",
                table: "reports",
                column: "run_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "report_coverage_classes");

            migrationBuilder.DropTable(
                name: "report_coverage_packages");

            migrationBuilder.DropTable(
                name: "report_test_cases");

            migrationBuilder.DropTable(
                name: "reports");
        }
    }
}
