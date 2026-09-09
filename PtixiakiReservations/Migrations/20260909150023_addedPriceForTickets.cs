using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PtixiakiReservations.Migrations
{
    /// <inheritdoc />
    public partial class addedPriceForTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "TicketPrice",
                table: "Event",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TicketPrice",
                table: "Event");
        }
    }
}
