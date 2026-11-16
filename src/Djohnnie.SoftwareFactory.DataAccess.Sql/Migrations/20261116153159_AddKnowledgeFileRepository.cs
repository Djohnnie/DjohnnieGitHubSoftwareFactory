using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations;

/// <inheritdoc />
public partial class AddKnowledgeFileRepository : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "RepositoryId",
            table: "KnowledgeFiles",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_KnowledgeFiles_RepositoryId",
            table: "KnowledgeFiles",
            column: "RepositoryId");

        migrationBuilder.AddForeignKey(
            name: "FK_KnowledgeFiles_Repositories_RepositoryId",
            table: "KnowledgeFiles",
            column: "RepositoryId",
            principalTable: "Repositories",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_KnowledgeFiles_Repositories_RepositoryId",
            table: "KnowledgeFiles");

        migrationBuilder.DropIndex(
            name: "IX_KnowledgeFiles_RepositoryId",
            table: "KnowledgeFiles");

        migrationBuilder.DropColumn(
            name: "RepositoryId",
            table: "KnowledgeFiles");
    }
}
