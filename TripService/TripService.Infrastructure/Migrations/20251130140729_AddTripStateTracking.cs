using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTripStateTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Trips",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DriverAcceptedAt",
                table: "Trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DriverArrivedAt",
                table: "Trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DriverAssignedAt",
                table: "Trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DriverRetryCount",
                table: "Trips",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastStatusChangeAt",
                table: "Trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TripCompletedAt",
                table: "Trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TripStartedAt",
                table: "Trips",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "DriverAcceptedAt",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "DriverArrivedAt",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "DriverAssignedAt",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "DriverRetryCount",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "LastStatusChangeAt",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "TripCompletedAt",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "TripStartedAt",
                table: "Trips");
        }
    }
}
