using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations
{
    /// <inheritdoc />
    public partial class AddCachedInputTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CachedInputPricePerMillion",
                table: "InternalModels",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CachedInputPricePerMillion",
                table: "InternalAgentUsages",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            // Requests recorded so far charged any cached tokens at the input price.
            migrationBuilder.Sql("UPDATE [InternalAgentUsages] SET [CachedInputPricePerMillion] = [InputPricePerMillion];");

            migrationBuilder.AddColumn<long>(
                name: "CachedInputTokens",
                table: "InternalAgentUsages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalCachedInputTokens",
                table: "InternalAgents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CachedInputPricePerMillion",
                table: "InternalModels");

            migrationBuilder.DropColumn(
                name: "CachedInputPricePerMillion",
                table: "InternalAgentUsages");

            migrationBuilder.DropColumn(
                name: "CachedInputTokens",
                table: "InternalAgentUsages");

            migrationBuilder.DropColumn(
                name: "TotalCachedInputTokens",
                table: "InternalAgents");
        }
    }
}
