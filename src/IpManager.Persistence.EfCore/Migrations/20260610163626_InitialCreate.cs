using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IpManager.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MacAddress = table.Column<string>(type: "nvarchar(17)", maxLength: 17, nullable: false),
                    MacNormalized = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Hostname = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeviceName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeviceType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Department = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FirstSeen = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeen = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HasInternetAccess = table.Column<bool>(type: "bit", nullable: false),
                    LastInternetActivity = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RelatedIp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RelatedMac = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DevicesFound = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InternetActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    IpAddressId = table.Column<int>(type: "int", nullable: false),
                    ActivityTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DestinationIp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BytesIn = table.Column<long>(type: "bigint", nullable: false),
                    BytesOut = table.Column<long>(type: "bigint", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternetActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InternetActivities_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IpAddresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Address = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: false),
                    IpNumeric = table.Column<long>(type: "bigint", nullable: false),
                    Subnet = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FirstSeen = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSeen = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentDeviceId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IpAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IpAddresses_Devices_CurrentDeviceId",
                        column: x => x.CurrentDeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Conflicts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IpAddressId = table.Column<int>(type: "int", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conflicts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Conflicts_IpAddresses_IpAddressId",
                        column: x => x.IpAddressId,
                        principalTable: "IpAddresses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IpBindings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IpAddressId = table.Column<int>(type: "int", nullable: false),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    LeaseStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeaseEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MissedScans = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IpBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IpBindings_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IpBindings_IpAddresses_IpAddressId",
                        column: x => x.IpAddressId,
                        principalTable: "IpAddresses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConflictDevices",
                columns: table => new
                {
                    ConflictId = table.Column<int>(type: "int", nullable: false),
                    DeviceId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConflictDevices", x => new { x.ConflictId, x.DeviceId });
                    table.ForeignKey(
                        name: "FK_ConflictDevices_Conflicts_ConflictId",
                        column: x => x.ConflictId,
                        principalTable: "Conflicts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConflictDevices_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConflictDevices_DeviceId",
                table: "ConflictDevices",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_Conflicts_IpAddressId_IsResolved",
                table: "Conflicts",
                columns: new[] { "IpAddressId", "IsResolved" });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_MacNormalized",
                table: "Devices",
                column: "MacNormalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InternetActivities_ActivityTime",
                table: "InternetActivities",
                column: "ActivityTime");

            migrationBuilder.CreateIndex(
                name: "IX_InternetActivities_DeviceId_ActivityTime",
                table: "InternetActivities",
                columns: new[] { "DeviceId", "ActivityTime" });

            migrationBuilder.CreateIndex(
                name: "IX_IpAddresses_Address",
                table: "IpAddresses",
                column: "Address",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IpAddresses_CurrentDeviceId",
                table: "IpAddresses",
                column: "CurrentDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_IpAddresses_IpNumeric",
                table: "IpAddresses",
                column: "IpNumeric");

            migrationBuilder.CreateIndex(
                name: "IX_IpBindings_DeviceId_IsActive",
                table: "IpBindings",
                columns: new[] { "DeviceId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_IpBindings_IpAddressId_IsActive",
                table: "IpBindings",
                columns: new[] { "IpAddressId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CreatedAt",
                table: "Notifications",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConflictDevices");

            migrationBuilder.DropTable(
                name: "InternetActivities");

            migrationBuilder.DropTable(
                name: "IpBindings");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "ScanRuns");

            migrationBuilder.DropTable(
                name: "Conflicts");

            migrationBuilder.DropTable(
                name: "IpAddresses");

            migrationBuilder.DropTable(
                name: "Devices");
        }
    }
}
