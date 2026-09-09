using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PtixiakiReservations.Migrations
{
    /// <inheritdoc />
    public partial class AddedPdfDocumentPaths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EventManagerRequestDocumentPath",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuperOrganizerRequestDocumentPath",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VenueManagerRequestDocumentPath",
                table: "AspNetUsers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EventManagerRequestDocumentPath",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SuperOrganizerRequestDocumentPath",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "VenueManagerRequestDocumentPath",
                table: "AspNetUsers");
        }
    }
}
