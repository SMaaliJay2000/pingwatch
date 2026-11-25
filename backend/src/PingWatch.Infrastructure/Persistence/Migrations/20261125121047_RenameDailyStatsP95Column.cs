using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PingWatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameDailyStatsP95Column : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "p95ms",
                table: "daily_stats",
                newName: "p95_ms");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "p95_ms",
                table: "daily_stats",
                newName: "p95ms");
        }
    }
}
