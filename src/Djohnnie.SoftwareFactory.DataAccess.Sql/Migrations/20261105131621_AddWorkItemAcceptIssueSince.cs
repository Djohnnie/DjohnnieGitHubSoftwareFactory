using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkItemAcceptIssueSince : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AcceptIssueSince",
                table: "WorkItems",
                type: "datetimeoffset",
                nullable: true);

            // The previous version marked an interrupted acceptance with AcceptStartedAt = DateTimeOffset.MinValue.
            migrationBuilder.Sql("UPDATE [WorkItems] SET [AcceptIssueSince] = [AcceptStartedAt], [AcceptStartedAt] = NULL WHERE [AcceptStartedAt] = '0001-01-01T00:00:00+00:00'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptIssueSince",
                table: "WorkItems");
        }
    }
}
