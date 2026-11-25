using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PingWatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    plan = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    billing_customer_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    subscription_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "alert_channels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    config_json = table.Column<string>(type: "text", nullable: false),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alert_channels", x => x.id);
                    table.UniqueConstraint("ak_alert_channels_id_account_id", x => new { x.id, x.account_id });
                    table.ForeignKey(
                        name: "fk_alert_channels_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "monitors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    port = table.Column<int>(type: "integer", nullable: true),
                    method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    expected_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    keyword = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    keyword_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    timeout_ms = table.Column<int>(type: "integer", nullable: false),
                    interval_sec = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitors", x => x.id);
                    table.UniqueConstraint("ak_monitors_id_account_id", x => new { x.id, x.account_id });
                    table.ForeignKey(
                        name: "fk_monitors_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "status_pages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    custom_domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    logo_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    is_public = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_status_pages", x => x.id);
                    table.UniqueConstraint("ak_status_pages_id_account_id", x => new { x.id, x.account_id });
                    table.ForeignKey(
                        name: "fk_status_pages_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_verified = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_users_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "check_results",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_up = table.Column<bool>(type: "boolean", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    response_ms = table.Column<int>(type: "integer", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    region = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_check_results", x => new { x.id, x.checked_at });
                    table.ForeignKey(
                        name: "fk_check_results_monitors_monitor_id",
                        column: x => x.monitor_id,
                        principalTable: "monitors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "daily_stats",
                columns: table => new
                {
                    monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checks = table.Column<int>(type: "integer", nullable: false),
                    failures = table.Column<int>(type: "integer", nullable: false),
                    uptime_pct = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    avg_ms = table.Column<int>(type: "integer", nullable: true),
                    p95ms = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_stats", x => new { x.monitor_id, x.date });
                    table.ForeignKey(
                        name: "fk_daily_stats_monitors_monitor_id_account_id",
                        columns: x => new { x.monitor_id, x.account_id },
                        principalTable: "monitors",
                        principalColumns: new[] { "id", "account_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "incidents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cause = table.Column<string>(type: "text", nullable: false),
                    duration_sec = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incidents", x => x.id);
                    table.UniqueConstraint("ak_incidents_id_account_id", x => new { x.id, x.account_id });
                    table.ForeignKey(
                        name: "fk_incidents_monitors_monitor_id_account_id",
                        columns: x => new { x.monitor_id, x.account_id },
                        principalTable: "monitors",
                        principalColumns: new[] { "id", "account_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "monitor_channels",
                columns: table => new
                {
                    monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitor_channels", x => new { x.monitor_id, x.channel_id });
                    table.ForeignKey(
                        name: "fk_monitor_channels_alert_channels_channel_id_account_id",
                        columns: x => new { x.channel_id, x.account_id },
                        principalTable: "alert_channels",
                        principalColumns: new[] { "id", "account_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_monitor_channels_monitors_monitor_id_account_id",
                        columns: x => new { x.monitor_id, x.account_id },
                        principalTable: "monitors",
                        principalColumns: new[] { "id", "account_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "status_page_monitors",
                columns: table => new
                {
                    status_page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_status_page_monitors", x => new { x.status_page_id, x.monitor_id });
                    table.ForeignKey(
                        name: "fk_status_page_monitors_monitors_monitor_id_account_id",
                        columns: x => new { x.monitor_id, x.account_id },
                        principalTable: "monitors",
                        principalColumns: new[] { "id", "account_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_status_page_monitors_status_pages_status_page_id_account_id",
                        columns: x => new { x.status_page_id, x.account_id },
                        principalTable: "status_pages",
                        principalColumns: new[] { "id", "account_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "alert_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alert_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_alert_log_alert_channels_channel_id_account_id",
                        columns: x => new { x.channel_id, x.account_id },
                        principalTable: "alert_channels",
                        principalColumns: new[] { "id", "account_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_alert_log_incidents_incident_id_account_id",
                        columns: x => new { x.incident_id, x.account_id },
                        principalTable: "incidents",
                        principalColumns: new[] { "id", "account_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_alert_log_monitors_monitor_id_account_id",
                        columns: x => new { x.monitor_id, x.account_id },
                        principalTable: "monitors",
                        principalColumns: new[] { "id", "account_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_billing_customer_id",
                table: "accounts",
                column: "billing_customer_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_alert_channels_account_id",
                table: "alert_channels",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_alert_log_channel_id_account_id",
                table: "alert_log",
                columns: new[] { "channel_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_alert_log_incident_id_account_id",
                table: "alert_log",
                columns: new[] { "incident_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_alert_log_monitor_id_account_id",
                table: "alert_log",
                columns: new[] { "monitor_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_check_results_monitor_id_checked_at",
                table: "check_results",
                columns: new[] { "monitor_id", "checked_at" });

            migrationBuilder.CreateIndex(
                name: "ix_daily_stats_monitor_id_account_id",
                table: "daily_stats",
                columns: new[] { "monitor_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_incidents_monitor_id_account_id",
                table: "incidents",
                columns: new[] { "monitor_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_incidents_one_open_per_monitor",
                table: "incidents",
                column: "monitor_id",
                unique: true,
                filter: "resolved_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_monitor_channels_channel_id_account_id",
                table: "monitor_channels",
                columns: new[] { "channel_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_monitor_channels_monitor_id_account_id",
                table: "monitor_channels",
                columns: new[] { "monitor_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_monitors_account_id",
                table: "monitors",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitors_next_check_at",
                table: "monitors",
                column: "next_check_at");

            migrationBuilder.CreateIndex(
                name: "ix_status_page_monitors_monitor_id_account_id",
                table: "status_page_monitors",
                columns: new[] { "monitor_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_status_page_monitors_status_page_id_account_id",
                table: "status_page_monitors",
                columns: new[] { "status_page_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_status_pages_account_id",
                table: "status_pages",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_status_pages_custom_domain",
                table: "status_pages",
                column: "custom_domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_status_pages_slug",
                table: "status_pages",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user_id",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_account_id",
                table: "users",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email",
                table: "users",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_user_name",
                table: "users",
                column: "normalized_user_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_log");

            migrationBuilder.DropTable(
                name: "check_results");

            migrationBuilder.DropTable(
                name: "daily_stats");

            migrationBuilder.DropTable(
                name: "monitor_channels");

            migrationBuilder.DropTable(
                name: "status_page_monitors");

            migrationBuilder.DropTable(
                name: "user_claims");

            migrationBuilder.DropTable(
                name: "user_logins");

            migrationBuilder.DropTable(
                name: "user_tokens");

            migrationBuilder.DropTable(
                name: "incidents");

            migrationBuilder.DropTable(
                name: "alert_channels");

            migrationBuilder.DropTable(
                name: "status_pages");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "monitors");

            migrationBuilder.DropTable(
                name: "accounts");
        }
    }
}
