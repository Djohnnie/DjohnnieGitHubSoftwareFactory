using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations;

/// <inheritdoc />
public partial class AddAgentRunRevisions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "FeedbackUntil",
            table: "AgentRuns",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Kind",
            table: "AgentRuns",
            type: "nvarchar(20)",
            maxLength: 20,
            nullable: false,
            // Every task before revisions existed opened a pull request.
            defaultValue: "Implement");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "FeedbackUntil",
            table: "AgentRuns");

        migrationBuilder.DropColumn(
            name: "Kind",
            table: "AgentRuns");
    }
}
