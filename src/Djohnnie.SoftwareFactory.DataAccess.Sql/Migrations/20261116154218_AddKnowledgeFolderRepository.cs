using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations;

/// <inheritdoc />
public partial class AddKnowledgeFolderRepository : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_KnowledgeFolders_ParentId_Name",
            table: "KnowledgeFolders");

        // Nullable first: the existing folders get their repository from the files in them below, then it becomes required.
        migrationBuilder.AddColumn<Guid>(
            name: "RepositoryId",
            table: "KnowledgeFolders",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.Sql(
            """
            -- Global files are never in a folder anymore: they move to the top of the library.
            UPDATE KnowledgeFiles SET FolderId = NULL WHERE RepositoryId IS NULL AND FolderId IS NOT NULL;

            -- A folder belongs to the repository of the (first) file in it...
            UPDATE f SET RepositoryId = (SELECT TOP 1 x.RepositoryId FROM KnowledgeFiles x WHERE x.FolderId = f.Id ORDER BY x.SysId)
            FROM KnowledgeFolders f;

            -- ...or else of a folder below it, or else of the folder above it.
            DECLARE @changed int = 1;
            WHILE @changed > 0
            BEGIN
                UPDATE p SET RepositoryId = (SELECT TOP 1 c.RepositoryId FROM KnowledgeFolders c WHERE c.ParentId = p.Id AND c.RepositoryId IS NOT NULL ORDER BY c.SysId)
                FROM KnowledgeFolders p
                WHERE p.RepositoryId IS NULL AND EXISTS (SELECT 1 FROM KnowledgeFolders c WHERE c.ParentId = p.Id AND c.RepositoryId IS NOT NULL);
                SET @changed = @@ROWCOUNT;

                UPDATE c SET RepositoryId = p.RepositoryId
                FROM KnowledgeFolders c JOIN KnowledgeFolders p ON p.Id = c.ParentId
                WHERE c.RepositoryId IS NULL AND p.RepositoryId IS NOT NULL;
                SET @changed = @changed + @@ROWCOUNT;
            END;

            -- Files and folders in a folder of another repository move to the top of their own repository's library.
            UPDATE x SET FolderId = NULL
            FROM KnowledgeFiles x JOIN KnowledgeFolders f ON f.Id = x.FolderId
            WHERE x.RepositoryId <> f.RepositoryId;
            UPDATE c SET ParentId = NULL
            FROM KnowledgeFolders c JOIN KnowledgeFolders p ON p.Id = c.ParentId
            WHERE c.RepositoryId <> p.RepositoryId;

            -- Folders without a repository have no repository files in or below them, so they are empty: delete them, deepest first.
            WHILE EXISTS (SELECT 1 FROM KnowledgeFolders WHERE RepositoryId IS NULL)
            BEGIN
                DELETE f FROM KnowledgeFolders f
                WHERE f.RepositoryId IS NULL AND NOT EXISTS (SELECT 1 FROM KnowledgeFolders c WHERE c.ParentId = f.Id);
            END;

            -- Moving folders to the top can give two folders of a repository the same name there; keep them apart.
            WITH duplicates AS (
                SELECT Name, SysId, ROW_NUMBER() OVER (PARTITION BY RepositoryId, ParentId, Name ORDER BY SysId) AS Number
                FROM KnowledgeFolders)
            UPDATE duplicates SET Name = LEFT(Name, 85) + N' (' + CAST(SysId AS nvarchar(12)) + N')' WHERE Number > 1;
            """);

        migrationBuilder.AlterColumn<Guid>(
            name: "RepositoryId",
            table: "KnowledgeFolders",
            type: "uniqueidentifier",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uniqueidentifier",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_KnowledgeFolders_ParentId",
            table: "KnowledgeFolders",
            column: "ParentId");

        migrationBuilder.CreateIndex(
            name: "IX_KnowledgeFolders_RepositoryId_ParentId_Name",
            table: "KnowledgeFolders",
            columns: new[] { "RepositoryId", "ParentId", "Name" },
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_KnowledgeFolders_Repositories_RepositoryId",
            table: "KnowledgeFolders",
            column: "RepositoryId",
            principalTable: "Repositories",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_KnowledgeFolders_Repositories_RepositoryId",
            table: "KnowledgeFolders");

        migrationBuilder.DropIndex(
            name: "IX_KnowledgeFolders_ParentId",
            table: "KnowledgeFolders");

        migrationBuilder.DropIndex(
            name: "IX_KnowledgeFolders_RepositoryId_ParentId_Name",
            table: "KnowledgeFolders");

        migrationBuilder.DropColumn(
            name: "RepositoryId",
            table: "KnowledgeFolders");

        migrationBuilder.CreateIndex(
            name: "IX_KnowledgeFolders_ParentId_Name",
            table: "KnowledgeFolders",
            columns: new[] { "ParentId", "Name" },
            unique: true);
    }
}
