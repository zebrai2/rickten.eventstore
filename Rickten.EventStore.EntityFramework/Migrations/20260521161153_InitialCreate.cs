using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rickten.EventStore.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StreamType = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StreamIdentifier = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    EventData = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Metadata = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Projections",
                columns: table => new
                {
                    Namespace = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ProjectionKey = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    GlobalPosition = table.Column<long>(type: "bigint", nullable: false),
                    StateType = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    State = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projections", x => new { x.Namespace, x.ProjectionKey });
                });

            migrationBuilder.CreateTable(
                name: "Snapshots",
                columns: table => new
                {
                    StreamType = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StreamIdentifier = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    StateType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    State = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Snapshots", x => new { x.StreamType, x.StreamIdentifier });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_GlobalPosition",
                table: "Events",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Events_Stream",
                table: "Events",
                columns: new[] { "StreamType", "StreamIdentifier" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_Stream_Version",
                table: "Events",
                columns: new[] { "StreamType", "StreamIdentifier", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "Projections");

            migrationBuilder.DropTable(
                name: "Snapshots");
        }
    }
}
