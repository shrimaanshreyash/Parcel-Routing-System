using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParcelRoutingSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationsHistoryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_routing_decisions_approval_state_decided_at_utc",
                table: "routing_decisions",
                columns: new[] { "approval_state", "decided_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_routing_decisions_decided_at_utc",
                table: "routing_decisions",
                column: "decided_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_occurred_at_utc",
                table: "audit_events",
                column: "occurred_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_routing_decisions_approval_state_decided_at_utc",
                table: "routing_decisions");

            migrationBuilder.DropIndex(
                name: "IX_routing_decisions_decided_at_utc",
                table: "routing_decisions");

            migrationBuilder.DropIndex(
                name: "IX_audit_events_occurred_at_utc",
                table: "audit_events");
        }
    }
}
