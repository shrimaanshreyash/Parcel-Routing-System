using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParcelRoutingSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3CountryProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "destination_country",
                table: "parcel_batches",
                type: "character(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character(2)",
                oldFixedLength: true,
                oldMaxLength: 2);

            migrationBuilder.AddColumn<string>(
                name: "country_source",
                table: "parcel_batch_rows",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "ManifestFallback");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "country_source",
                table: "parcel_batch_rows");

            migrationBuilder.AlterColumn<string>(
                name: "destination_country",
                table: "parcel_batches",
                type: "character(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character(2)",
                oldFixedLength: true,
                oldMaxLength: 2,
                oldNullable: true);
        }
    }
}
