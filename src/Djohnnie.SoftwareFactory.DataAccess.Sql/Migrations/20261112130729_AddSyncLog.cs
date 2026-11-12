using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations;

/// <inheritdoc />
public partial class AddSyncLog : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SyncLogEvents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                LastOccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Iterations = table.Column<int>(type: "int", nullable: false),
                Level = table.Column<int>(type: "int", nullable: false),
                Kind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                RepositoryFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                IssueNumber = table.Column<int>(type: "int", nullable: true),
                WorkItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                Detail = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                SysId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SyncLogEvents", x => x.Id)
                    .Annotation("SqlServer:Clustered", false);
                table.UniqueConstraint("AK_SyncLogEvents_SysId", x => x.SysId)
                    .Annotation("SqlServer:Clustered", true);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SyncLogEvents_LastOccurredAt",
            table: "SyncLogEvents",
            column: "LastOccurredAt");

        migrationBuilder.CreateIndex(
            name: "IX_SyncLogEvents_Level",
            table: "SyncLogEvents",
            column: "Level");

        migrationBuilder.CreateIndex(
            name: "IX_SyncLogEvents_RepositoryFullName",
            table: "SyncLogEvents",
            column: "RepositoryFullName");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "SyncLogEvents");
    }
}
