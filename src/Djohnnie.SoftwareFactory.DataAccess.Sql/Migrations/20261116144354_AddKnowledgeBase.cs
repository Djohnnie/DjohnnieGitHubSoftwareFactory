using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations;

/// <inheritdoc />
public partial class AddKnowledgeBase : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "KnowledgeFolders",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                SysId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_KnowledgeFolders", x => x.Id)
                    .Annotation("SqlServer:Clustered", false);
                table.UniqueConstraint("AK_KnowledgeFolders_SysId", x => x.SysId)
                    .Annotation("SqlServer:Clustered", true);
                table.ForeignKey(
                    name: "FK_KnowledgeFolders_KnowledgeFolders_ParentId",
                    column: x => x.ParentId,
                    principalTable: "KnowledgeFolders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "KnowledgeFiles",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FolderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                ContentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Size = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                SysId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_KnowledgeFiles", x => x.Id)
                    .Annotation("SqlServer:Clustered", false);
                table.UniqueConstraint("AK_KnowledgeFiles_SysId", x => x.SysId)
                    .Annotation("SqlServer:Clustered", true);
                table.ForeignKey(
                    name: "FK_KnowledgeFiles_KnowledgeFolders_FolderId",
                    column: x => x.FolderId,
                    principalTable: "KnowledgeFolders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_KnowledgeFiles_FolderId",
            table: "KnowledgeFiles",
            column: "FolderId");

        migrationBuilder.CreateIndex(
            name: "IX_KnowledgeFolders_ParentId_Name",
            table: "KnowledgeFolders",
            columns: new[] { "ParentId", "Name" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "KnowledgeFiles");

        migrationBuilder.DropTable(
            name: "KnowledgeFolders");
    }
}
