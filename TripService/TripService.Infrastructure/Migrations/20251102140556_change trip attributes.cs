using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class changetripattributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RiderId",
                table: "Trips",
                newName: "PassengerId");

            migrationBuilder.RenameColumn(
                name: "DriverId",
                table: "Trips",
                newName: "AssignedDriverId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PassengerId",
                table: "Trips",
                newName: "RiderId");

            migrationBuilder.RenameColumn(
                name: "AssignedDriverId",
                table: "Trips",
                newName: "DriverId");
        }
    }
}
