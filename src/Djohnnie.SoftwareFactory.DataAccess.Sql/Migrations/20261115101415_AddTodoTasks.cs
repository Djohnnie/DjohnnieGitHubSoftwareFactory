using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations;

/// <inheritdoc />
public partial class AddTodoTasks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "GeneratedByTaskId",
            table: "WorkItems",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "TodoTasks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RootId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Kind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                RepositoryFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Result = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                Error = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                Attempts = table.Column<int>(type: "int", nullable: false),
                MaxAttempts = table.Column<int>(type: "int", nullable: false),
                NotBefore = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                LeaseUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                QueuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                SysId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TodoTasks", x => x.Id)
                    .Annotation("SqlServer:Clustered", false);
                table.UniqueConstraint("AK_TodoTasks_SysId", x => x.SysId)
                    .Annotation("SqlServer:Clustered", true);
            });

        migrationBuilder.CreateIndex(
            name: "IX_WorkItems_GeneratedByTaskId",
            table: "WorkItems",
            column: "GeneratedByTaskId");

        migrationBuilder.CreateIndex(
            name: "IX_TodoTasks_ParentId",
            table: "TodoTasks",
            column: "ParentId");

        migrationBuilder.CreateIndex(
            name: "IX_TodoTasks_RepositoryFullName_CreatedAt",
            table: "TodoTasks",
            columns: new[] { "RepositoryFullName", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_TodoTasks_RootId",
            table: "TodoTasks",
            column: "RootId");

        migrationBuilder.CreateIndex(
            name: "IX_TodoTasks_Status_NotBefore",
            table: "TodoTasks",
            columns: new[] { "Status", "NotBefore" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "TodoTasks");

        migrationBuilder.DropIndex(
            name: "IX_WorkItems_GeneratedByTaskId",
            table: "WorkItems");

        migrationBuilder.DropColumn(
            name: "GeneratedByTaskId",
            table: "WorkItems");
    }
}
