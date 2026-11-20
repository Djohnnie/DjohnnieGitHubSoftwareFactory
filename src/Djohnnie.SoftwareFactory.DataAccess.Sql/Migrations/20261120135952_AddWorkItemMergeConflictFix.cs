using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations;

/// <inheritdoc />
public partial class AddWorkItemMergeConflictFix : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "FixMergeConflicts",
            table: "WorkItems",
            type: "bit",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "MergeConflictFixRequestedAt",
            table: "WorkItems",
            type: "datetimeoffset",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "FixMergeConflicts",
            table: "WorkItems");

        migrationBuilder.DropColumn(
            name: "MergeConflictFixRequestedAt",
            table: "WorkItems");
    }
}
