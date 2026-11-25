using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PingWatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PartitionCheckResults : Migration
    {
        private const string Columns = """
            id bigserial NOT NULL,
            account_id uuid NOT NULL,
            monitor_id uuid NOT NULL,
            checked_at timestamp with time zone NOT NULL,
            is_up boolean NOT NULL,
            status_code integer NULL,
            response_ms integer NULL,
            error text NULL,
            region character varying(32) NOT NULL,
            CONSTRAINT pk_check_results PRIMARY KEY (id, checked_at),
            CONSTRAINT fk_check_results_monitors_monitor_id FOREIGN KEY (monitor_id)
                REFERENCES monitors (id) ON DELETE CASCADE
            """;

        private const string Index =
            "CREATE INDEX ix_check_results_monitor_id_checked_at ON check_results (monitor_id, checked_at);";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE check_results;");
            migrationBuilder.Sql($"CREATE TABLE check_results ({Columns}) PARTITION BY RANGE (checked_at);");
            migrationBuilder.Sql(Index);

            // Safety net so an insert never fails for lack of a partition.
            migrationBuilder.Sql("CREATE TABLE check_results_default PARTITION OF check_results DEFAULT;");

            migrationBuilder.Sql("""
                CREATE FUNCTION ensure_check_results_partition(for_date date) RETURNS void
                LANGUAGE plpgsql AS $$
                DECLARE
                    start_month date := date_trunc('month', for_date)::date;
                    partition_name text := 'check_results_' || to_char(start_month, 'YYYY_MM');
                BEGIN
                    EXECUTE format(
                        'CREATE TABLE IF NOT EXISTS %I PARTITION OF check_results FOR VALUES FROM (%L) TO (%L)',
                        partition_name,
                        start_month::timestamp AT TIME ZONE 'UTC',
                        (start_month + interval '1 month') AT TIME ZONE 'UTC');
                END;
                $$;
                """);

            // ponytail: current month + 3. The M4 rollup job must keep creating them ahead,
            // because a month can't be added once the default partition holds its rows.
            migrationBuilder.Sql("""
                SELECT ensure_check_results_partition(((now() AT TIME ZONE 'UTC') + make_interval(months => n))::date)
                FROM generate_series(0, 3) AS n;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back discards check results.
            migrationBuilder.Sql("DROP TABLE check_results;");
            migrationBuilder.Sql("DROP FUNCTION ensure_check_results_partition(date);");
            migrationBuilder.Sql($"CREATE TABLE check_results ({Columns});");
            migrationBuilder.Sql(Index);
        }
    }
}