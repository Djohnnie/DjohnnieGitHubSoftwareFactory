using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations
{
    /// <inheritdoc />
    public partial class AddReasoningTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ReasoningTokens",
                table: "InternalAgentUsages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalReasoningTokens",
                table: "InternalAgents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReasoningTokens",
                table: "InternalAgentUsages");

            migrationBuilder.DropColumn(
                name: "TotalReasoningTokens",
                table: "InternalAgents");
        }
    }
}
