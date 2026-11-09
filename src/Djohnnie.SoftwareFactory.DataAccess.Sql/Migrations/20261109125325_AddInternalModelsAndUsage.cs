using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations
{
    /// <inheritdoc />
    public partial class AddInternalModelsAndUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InternalModelId",
                table: "InternalAgents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RequestCount",
                table: "InternalAgents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalInputCost",
                table: "InternalAgents",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "TotalInputTokens",
                table: "InternalAgents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalOutputCost",
                table: "InternalAgents",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "TotalOutputTokens",
                table: "InternalAgents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "InternalAgentUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InternalAgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    InputPricePerMillion = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    OutputPricePerMillion = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    InputCost = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    OutputCost = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SysId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternalAgentUsages", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.UniqueConstraint("AK_InternalAgentUsages_SysId", x => x.SysId)
                        .Annotation("SqlServer:Clustered", true);
                    table.ForeignKey(
                        name: "FK_InternalAgentUsages_InternalAgents_InternalAgentId",
                        column: x => x.InternalAgentId,
                        principalTable: "InternalAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InternalModels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    InputPricePerMillion = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    OutputPricePerMillion = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SysId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternalModels", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.UniqueConstraint("AK_InternalModels_SysId", x => x.SysId)
                        .Annotation("SqlServer:Clustered", true);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InternalAgents_InternalModelId",
                table: "InternalAgents",
                column: "InternalModelId");

            migrationBuilder.CreateIndex(
                name: "IX_InternalAgentUsages_InternalAgentId",
                table: "InternalAgentUsages",
                column: "InternalAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_InternalModels_Name",
                table: "InternalModels",
                column: "Name",
                unique: true);

            // The free-text models become catalog entries (without a price yet), so the agents keep the model they run on.
            migrationBuilder.Sql("""
                INSERT INTO [InternalModels] ([Id], [Name], [Description], [InputPricePerMillion], [OutputPricePerMillion], [CreatedAt], [UpdatedAt])
                SELECT NEWID(), [Model], N'', 0, 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()
                FROM (SELECT DISTINCT [Model] FROM [InternalAgents] WHERE [Model] <> N'') AS [Models];

                UPDATE [Agent] SET [InternalModelId] = [Catalog].[Id]
                FROM [InternalAgents] AS [Agent]
                INNER JOIN [InternalModels] AS [Catalog] ON [Catalog].[Name] = [Agent].[Model];
                """);

            migrationBuilder.DropColumn(
                name: "Model",
                table: "InternalAgents");

            migrationBuilder.AddForeignKey(
                name: "FK_InternalAgents_InternalModels_InternalModelId",
                table: "InternalAgents",
                column: "InternalModelId",
                principalTable: "InternalModels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "InternalAgents",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE [Agent] SET [Model] = [Catalog].[Name]
                FROM [InternalAgents] AS [Agent]
                INNER JOIN [InternalModels] AS [Catalog] ON [Catalog].[Id] = [Agent].[InternalModelId];
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_InternalAgents_InternalModels_InternalModelId",
                table: "InternalAgents");

            migrationBuilder.DropTable(
                name: "InternalAgentUsages");

            migrationBuilder.DropTable(
                name: "InternalModels");

            migrationBuilder.DropIndex(
                name: "IX_InternalAgents_InternalModelId",
                table: "InternalAgents");

            migrationBuilder.DropColumn(
                name: "InternalModelId",
                table: "InternalAgents");

            migrationBuilder.DropColumn(
                name: "RequestCount",
                table: "InternalAgents");

            migrationBuilder.DropColumn(
                name: "TotalInputCost",
                table: "InternalAgents");

            migrationBuilder.DropColumn(
                name: "TotalInputTokens",
                table: "InternalAgents");

            migrationBuilder.DropColumn(
                name: "TotalOutputCost",
                table: "InternalAgents");

            migrationBuilder.DropColumn(
                name: "TotalOutputTokens",
                table: "InternalAgents");
        }
    }
}
