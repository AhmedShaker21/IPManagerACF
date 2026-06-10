using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IpManager.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class inti : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssetTag",
                table: "Devices",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cpu",
                table: "Devices",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsManaged",
                table: "Devices",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Devices",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperatingSystem",
                table: "Devices",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerEmail",
                table: "Devices",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerName",
                table: "Devices",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerPhone",
                table: "Devices",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RamGb",
                table: "Devices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StorageGb",
                table: "Devices",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssetTag",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "Cpu",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "IsManaged",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "OperatingSystem",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "OwnerEmail",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "OwnerName",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "OwnerPhone",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "RamGb",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "StorageGb",
                table: "Devices");
        }
    }
}
