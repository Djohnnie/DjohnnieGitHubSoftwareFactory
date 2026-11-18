using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations;

/// <inheritdoc />
public partial class AddProjectChats : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProjectChats",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RepositoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                LastUsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                SysId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectChats", x => x.Id)
                    .Annotation("SqlServer:Clustered", false);
                table.UniqueConstraint("AK_ProjectChats_SysId", x => x.SysId)
                    .Annotation("SqlServer:Clustered", true);
                table.ForeignKey(
                    name: "FK_ProjectChats_Repositories_RepositoryId",
                    column: x => x.RepositoryId,
                    principalTable: "Repositories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ProjectChatMessages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChatId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Sequence = table.Column<int>(type: "int", nullable: false),
                Role = table.Column<int>(type: "int", nullable: false),
                DisplayText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                Payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                IsError = table.Column<bool>(type: "bit", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                SysId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectChatMessages", x => x.Id)
                    .Annotation("SqlServer:Clustered", false);
                table.UniqueConstraint("AK_ProjectChatMessages_SysId", x => x.SysId)
                    .Annotation("SqlServer:Clustered", true);
                table.ForeignKey(
                    name: "FK_ProjectChatMessages_ProjectChats_ChatId",
                    column: x => x.ChatId,
                    principalTable: "ProjectChats",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectChatMessages_ChatId_Sequence",
            table: "ProjectChatMessages",
            columns: new[] { "ChatId", "Sequence" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ProjectChats_RepositoryId_LastUsedAt",
            table: "ProjectChats",
            columns: new[] { "RepositoryId", "LastUsedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ProjectChatMessages");

        migrationBuilder.DropTable(
            name: "ProjectChats");
    }
}
