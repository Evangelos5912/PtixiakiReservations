using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PtixiakiReservations.Migrations
{
    /// <inheritdoc />
    public partial class EventTypeImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImagePath",
                table: "EventType",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImagePath",
                table: "EventType");
        }
    }
}
