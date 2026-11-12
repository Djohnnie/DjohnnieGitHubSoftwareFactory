using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations;

/// <inheritdoc />
public partial class AddWorkItemPullRequestBranch : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PullRequestBranch",
            table: "WorkItems",
            type: "nvarchar(250)",
            maxLength: 250,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PullRequestBranch",
            table: "WorkItems");
    }
}
