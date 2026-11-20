using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations;

/// <inheritdoc />
public partial class AddWorkItemReviseOnCopilotReview : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "CopilotReviewHandledAt",
            table: "WorkItems",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "ReviseOnCopilotReview",
            table: "WorkItems",
            type: "bit",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CopilotReviewHandledAt",
            table: "WorkItems");

        migrationBuilder.DropColumn(
            name: "ReviseOnCopilotReview",
            table: "WorkItems");
    }
}
