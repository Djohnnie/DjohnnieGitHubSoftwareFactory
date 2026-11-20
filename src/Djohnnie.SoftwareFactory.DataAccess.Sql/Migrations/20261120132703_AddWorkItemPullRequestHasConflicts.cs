using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations;

/// <inheritdoc />
public partial class AddWorkItemPullRequestHasConflicts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "PullRequestHasConflicts",
            table: "WorkItems",
            type: "bit",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PullRequestHasConflicts",
            table: "WorkItems");
    }
}
