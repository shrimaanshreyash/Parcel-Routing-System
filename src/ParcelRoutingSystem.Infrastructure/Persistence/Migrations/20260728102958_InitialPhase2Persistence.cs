using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ParcelRoutingSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPhase2Persistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    subject_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    subject_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    details = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "parcel_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    request_fingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    destination_country = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    completed_rows = table.Column<int>(type: "integer", nullable: false),
                    failed_rows = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parcel_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "routing_rule_sets",
                columns: table => new
                {
                    version = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    activated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routing_rule_sets", x => x.version);
                });

            migrationBuilder.CreateTable(
                name: "parcel_batch_rows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_number = table.Column<int>(type: "integer", nullable: false),
                    weight_kilograms = table.Column<decimal>(type: "numeric(29,12)", precision: 29, scale: 12, nullable: false),
                    declared_value_euros = table.Column<decimal>(type: "numeric(29,12)", precision: 29, scale: 12, nullable: false),
                    destination_country = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    error_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    decision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    claim_token = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parcel_batch_rows", x => x.id);
                    table.ForeignKey(
                        name: "FK_parcel_batch_rows_parcel_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "parcel_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "routing_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    request_fingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    weight_kilograms = table.Column<decimal>(type: "numeric(29,12)", precision: 29, scale: 12, nullable: false),
                    declared_value_euros = table.Column<decimal>(type: "numeric(29,12)", precision: 29, scale: 12, nullable: false),
                    destination_country = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    intended_department = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    approval_state = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    rule_set_version = table.Column<int>(type: "integer", nullable: false),
                    matched_rule_ids = table.Column<string[]>(type: "text[]", nullable: false),
                    reasons = table.Column<string[]>(type: "text[]", nullable: false),
                    decided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    batch_row_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routing_decisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_routing_decisions_routing_rule_sets_rule_set_version",
                        column: x => x.rule_set_version,
                        principalTable: "routing_rule_sets",
                        principalColumn: "version",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "routing_insurance_rules",
                columns: table => new
                {
                    rule_set_version = table.Column<int>(type: "integer", nullable: false),
                    rule_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    threshold_exclusive_euros = table.Column<decimal>(type: "numeric(29,12)", precision: 29, scale: 12, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routing_insurance_rules", x => x.rule_set_version);
                    table.ForeignKey(
                        name: "FK_routing_insurance_rules_routing_rule_sets_rule_set_version",
                        column: x => x.rule_set_version,
                        principalTable: "routing_rule_sets",
                        principalColumn: "version",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "routing_weight_band_rules",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    rule_set_version = table.Column<int>(type: "integer", nullable: false),
                    rule_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    lower_bound_exclusive = table.Column<decimal>(type: "numeric(29,12)", precision: 29, scale: 12, nullable: false),
                    upper_bound_inclusive = table.Column<decimal>(type: "numeric(29,12)", precision: 29, scale: 12, nullable: true),
                    department = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routing_weight_band_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_routing_weight_band_rules_routing_rule_sets_rule_set_version",
                        column: x => x.rule_set_version,
                        principalTable: "routing_rule_sets",
                        principalColumn: "version",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "insurance_approvals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    approved_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    approved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_insurance_approvals", x => x.id);
                    table.ForeignKey(
                        name: "FK_insurance_approvals_routing_decisions_decision_id",
                        column: x => x.decision_id,
                        principalTable: "routing_decisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "routing_rule_sets",
                columns: new[] { "version", "activated_at_utc", "created_at_utc", "created_by", "status" },
                values: new object[] { 1, new DateTimeOffset(new DateTime(2026, 7, 28, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new DateTimeOffset(new DateTime(2026, 7, 28, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "system", "Active" });

            migrationBuilder.InsertData(
                table: "routing_insurance_rules",
                columns: new[] { "rule_set_version", "priority", "rule_id", "threshold_exclusive_euros" },
                values: new object[] { 1, 1000, "VALUE-INSURANCE-OVER-1000-EUR", 1000m });

            migrationBuilder.InsertData(
                table: "routing_weight_band_rules",
                columns: new[] { "id", "department", "lower_bound_exclusive", "priority", "rule_id", "rule_set_version", "upper_bound_inclusive" },
                values: new object[,]
                {
                    { 1L, "Mail", 0m, 100, "WEIGHT-MAIL-UP-TO-1-KG", 1, 1m },
                    { 2L, "Regular", 1m, 200, "WEIGHT-REGULAR-UP-TO-10-KG", 1, 10m },
                    { 3L, "Heavy", 10m, 300, "WEIGHT-HEAVY-OVER-10-KG", 1, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_event_type_idempotency_key",
                table: "audit_events",
                columns: new[] { "event_type", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_subject_type_subject_id",
                table: "audit_events",
                columns: new[] { "subject_type", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "IX_insurance_approvals_decision_id",
                table: "insurance_approvals",
                column: "decision_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_insurance_approvals_idempotency_key",
                table: "insurance_approvals",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_parcel_batch_rows_batch_id_row_number",
                table: "parcel_batch_rows",
                columns: new[] { "batch_id", "row_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_parcel_batch_rows_status_lease_expires_at_utc",
                table: "parcel_batch_rows",
                columns: new[] { "status", "lease_expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_parcel_batches_idempotency_key",
                table: "parcel_batches",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_routing_decisions_batch_row_id",
                table: "routing_decisions",
                column: "batch_row_id",
                unique: true,
                filter: "batch_row_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_routing_decisions_idempotency_key",
                table: "routing_decisions",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_routing_decisions_rule_set_version",
                table: "routing_decisions",
                column: "rule_set_version");

            migrationBuilder.CreateIndex(
                name: "IX_routing_rule_sets_status",
                table: "routing_rule_sets",
                column: "status",
                unique: true,
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_routing_weight_band_rules_rule_set_version_priority",
                table: "routing_weight_band_rules",
                columns: new[] { "rule_set_version", "priority" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_routing_weight_band_rules_rule_set_version_rule_id",
                table: "routing_weight_band_rules",
                columns: new[] { "rule_set_version", "rule_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "insurance_approvals");

            migrationBuilder.DropTable(
                name: "parcel_batch_rows");

            migrationBuilder.DropTable(
                name: "routing_insurance_rules");

            migrationBuilder.DropTable(
                name: "routing_weight_band_rules");

            migrationBuilder.DropTable(
                name: "routing_decisions");

            migrationBuilder.DropTable(
                name: "parcel_batches");

            migrationBuilder.DropTable(
                name: "routing_rule_sets");
        }
    }
}
