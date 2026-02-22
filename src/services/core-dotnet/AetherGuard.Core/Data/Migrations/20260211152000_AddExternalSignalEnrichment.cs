using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AetherGuard.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalSignalEnrichment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "summary_digest",
                table: "external_signals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "summary_digest_truncated",
                table: "external_signals",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "summary_schema_version",
                table: "external_signals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "enrichment_schema_version",
                table: "external_signals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "sentiment_negative",
                table: "external_signals",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "sentiment_neutral",
                table: "external_signals",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "sentiment_positive",
                table: "external_signals",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "volatility_probability",
                table: "external_signals",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "supply_bias",
                table: "external_signals",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "summarized_at",
                table: "external_signals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "enriched_at",
                table: "external_signals",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "summary_digest",
                table: "external_signals");

            migrationBuilder.DropColumn(
                name: "summary_digest_truncated",
                table: "external_signals");

            migrationBuilder.DropColumn(
                name: "summary_schema_version",
                table: "external_signals");

            migrationBuilder.DropColumn(
                name: "enrichment_schema_version",
                table: "external_signals");

            migrationBuilder.DropColumn(
                name: "sentiment_negative",
                table: "external_signals");

            migrationBuilder.DropColumn(
                name: "sentiment_neutral",
                table: "external_signals");

            migrationBuilder.DropColumn(
                name: "sentiment_positive",
                table: "external_signals");

            migrationBuilder.DropColumn(
                name: "volatility_probability",
                table: "external_signals");

            migrationBuilder.DropColumn(
                name: "supply_bias",
                table: "external_signals");

            migrationBuilder.DropColumn(
                name: "summarized_at",
                table: "external_signals");

            migrationBuilder.DropColumn(
                name: "enriched_at",
                table: "external_signals");
        }
    }
}
