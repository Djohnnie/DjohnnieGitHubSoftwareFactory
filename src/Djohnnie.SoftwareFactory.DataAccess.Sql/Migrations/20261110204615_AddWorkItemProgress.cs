using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations;

/// <inheritdoc />
public partial class AddWorkItemProgress : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AgentRunUrl",
            table: "WorkItems",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Progress",
            table: "WorkItems",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "PullRequestChecks",
            table: "WorkItems",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "PullRequestNumber",
            table: "WorkItems",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PullRequestUrl",
            table: "WorkItems",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AgentRunUrl",
            table: "WorkItems");

        migrationBuilder.DropColumn(
            name: "Progress",
            table: "WorkItems");

        migrationBuilder.DropColumn(
            name: "PullRequestChecks",
            table: "WorkItems");

        migrationBuilder.DropColumn(
            name: "PullRequestNumber",
            table: "WorkItems");

        migrationBuilder.DropColumn(
            name: "PullRequestUrl",
            table: "WorkItems");
    }
}
