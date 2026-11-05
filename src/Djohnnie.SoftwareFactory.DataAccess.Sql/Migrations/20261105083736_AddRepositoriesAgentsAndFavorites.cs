using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations
{
    /// <summary>
    /// Replaces <c>RepositoryAccesses</c> with a <c>Repositories</c> table that everything repository-specific references,
    /// adds local <c>Agents</c> rows, and favorite flags. Existing access history and epics are carried over.
    /// </summary>
    public partial class AddRepositoriesAgentsAndFavorites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Repositories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastAccessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsFavorite = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repositories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_FullName",
                table: "Repositories",
                column: "FullName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_LastAccessedAt",
                table: "Repositories",
                column: "LastAccessedAt");

            // Carry over the access history (full names there are already lower case).
            migrationBuilder.Sql("""
                INSERT INTO [Repositories] ([FullName], [Name], [LastAccessedAt], [IsFavorite])
                SELECT [FullName], SUBSTRING([FullName], CHARINDEX('/', [FullName]) + 1, 100), [LastAccessedAt], 0
                FROM [RepositoryAccesses];
                """);

            // Repositories that only have epics (never recorded as accessed).
            migrationBuilder.Sql("""
                INSERT INTO [Repositories] ([FullName], [Name], [LastAccessedAt], [IsFavorite])
                SELECT [e].[FullName], SUBSTRING([e].[FullName], CHARINDEX('/', [e].[FullName]) + 1, 100), NULL, 0
                FROM (SELECT DISTINCT LOWER(LTRIM(RTRIM([RepositoryFullName]))) AS [FullName] FROM [Epics]) AS [e]
                WHERE NOT EXISTS (SELECT 1 FROM [Repositories] AS [r] WHERE [r].[FullName] = [e].[FullName]);
                """);

            migrationBuilder.DropTable(
                name: "RepositoryAccesses");

            migrationBuilder.AddColumn<int>(
                name: "RepositoryId",
                table: "Epics",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE [e] SET [e].[RepositoryId] = [r].[Id]
                FROM [Epics] AS [e]
                INNER JOIN [Repositories] AS [r] ON [r].[FullName] = LOWER(LTRIM(RTRIM([e].[RepositoryFullName])));
                """);

            migrationBuilder.DropIndex(
                name: "IX_Epics_RepositoryFullName_Status",
                table: "Epics");

            migrationBuilder.DropColumn(
                name: "RepositoryFullName",
                table: "Epics");

            migrationBuilder.CreateIndex(
                name: "IX_Epics_RepositoryId_Status",
                table: "Epics",
                columns: new[] { "RepositoryId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_Epics_Repositories_RepositoryId",
                table: "Epics",
                column: "RepositoryId",
                principalTable: "Repositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateTable(
                name: "Agents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RepositoryId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsFavorite = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Agents_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Agents_RepositoryId_FileName",
                table: "Agents",
                columns: new[] { "RepositoryId", "FileName" },
                unique: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite",
                table: "AiModels",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFavorite",
                table: "AiModels");

            migrationBuilder.DropTable(
                name: "Agents");

            migrationBuilder.AddColumn<string>(
                name: "RepositoryFullName",
                table: "Epics",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE [e] SET [e].[RepositoryFullName] = [r].[FullName]
                FROM [Epics] AS [e]
                INNER JOIN [Repositories] AS [r] ON [r].[Id] = [e].[RepositoryId];
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Epics_Repositories_RepositoryId",
                table: "Epics");

            migrationBuilder.DropIndex(
                name: "IX_Epics_RepositoryId_Status",
                table: "Epics");

            migrationBuilder.DropColumn(
                name: "RepositoryId",
                table: "Epics");

            migrationBuilder.CreateIndex(
                name: "IX_Epics_RepositoryFullName_Status",
                table: "Epics",
                columns: new[] { "RepositoryFullName", "Status" });

            migrationBuilder.CreateTable(
                name: "RepositoryAccesses",
                columns: table => new
                {
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LastAccessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryAccesses", x => x.FullName);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryAccesses_LastAccessedAt",
                table: "RepositoryAccesses",
                column: "LastAccessedAt");

            migrationBuilder.Sql("""
                INSERT INTO [RepositoryAccesses] ([FullName], [LastAccessedAt])
                SELECT [FullName], [LastAccessedAt] FROM [Repositories] WHERE [LastAccessedAt] IS NOT NULL;
                """);

            migrationBuilder.DropTable(
                name: "Repositories");
        }
    }
}
