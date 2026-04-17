using Microsoft.EntityFrameworkCore.Migrations;

namespace Rickten.EventStore.EntityFramework.Migrations;

/// <summary>
/// Adds Namespace column to Projections table and migrates existing data to "system" namespace.
/// Changes primary key from ProjectionKey to composite key (Namespace, ProjectionKey).
/// </summary>
public partial class AddProjectionNamespace : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Add Namespace column with default value "system"
        migrationBuilder.AddColumn<string>(
            name: "Namespace",
            table: "Projections",
            type: "nvarchar(255)",
            maxLength: 255,
            nullable: false,
            defaultValue: "system");

        // Drop the old primary key
        migrationBuilder.DropPrimaryKey(
            name: "PK_Projections",
            table: "Projections");

        // Create new composite primary key
        migrationBuilder.AddPrimaryKey(
            name: "PK_Projections",
            table: "Projections",
            columns: new[] { "Namespace", "ProjectionKey" });

        // Update all existing records to use "system" namespace
        migrationBuilder.Sql("UPDATE Projections SET Namespace = 'system' WHERE Namespace IS NULL OR Namespace = ''");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Drop the composite primary key
        migrationBuilder.DropPrimaryKey(
            name: "PK_Projections",
            table: "Projections");

        // Recreate the original primary key
        migrationBuilder.AddPrimaryKey(
            name: "PK_Projections",
            table: "Projections",
            column: "ProjectionKey");

        // Remove Namespace column
        migrationBuilder.DropColumn(
            name: "Namespace",
            table: "Projections");
    }
}
