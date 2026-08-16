using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PtixiakiReservations.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UnitGrouId",
                table: "Seat",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnitGroupId",
                table: "Seat",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnitGrouId",
                table: "NonSelectable",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnitGroupId",
                table: "NonSelectable",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UnitGroup",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: true),
                    Top = table.Column<decimal>(type: "numeric", nullable: false),
                    Left = table.Column<decimal>(type: "numeric", nullable: false),
                    LayoutId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitGroup", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnitGroup_Layout_LayoutId",
                        column: x => x.LayoutId,
                        principalTable: "Layout",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Seat_UnitGrouId",
                table: "Seat",
                column: "UnitGrouId");

            migrationBuilder.CreateIndex(
                name: "IX_NonSelectable_UnitGrouId",
                table: "NonSelectable",
                column: "UnitGrouId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitGroup_LayoutId",
                table: "UnitGroup",
                column: "LayoutId");

            migrationBuilder.AddForeignKey(
                name: "FK_NonSelectable_UnitGroup_UnitGrouId",
                table: "NonSelectable",
                column: "UnitGrouId",
                principalTable: "UnitGroup",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Seat_UnitGroup_UnitGrouId",
                table: "Seat",
                column: "UnitGrouId",
                principalTable: "UnitGroup",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NonSelectable_UnitGroup_UnitGrouId",
                table: "NonSelectable");

            migrationBuilder.DropForeignKey(
                name: "FK_Seat_UnitGroup_UnitGrouId",
                table: "Seat");

            migrationBuilder.DropTable(
                name: "UnitGroup");

            migrationBuilder.DropIndex(
                name: "IX_Seat_UnitGrouId",
                table: "Seat");

            migrationBuilder.DropIndex(
                name: "IX_NonSelectable_UnitGrouId",
                table: "NonSelectable");

            migrationBuilder.DropColumn(
                name: "UnitGrouId",
                table: "Seat");

            migrationBuilder.DropColumn(
                name: "UnitGroupId",
                table: "Seat");

            migrationBuilder.DropColumn(
                name: "UnitGrouId",
                table: "NonSelectable");

            migrationBuilder.DropColumn(
                name: "UnitGroupId",
                table: "NonSelectable");
        }
    }
}
