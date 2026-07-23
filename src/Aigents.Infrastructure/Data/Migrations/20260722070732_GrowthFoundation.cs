using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aigents.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class GrowthFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ListingInquiries_Agents_AgentId",
                table: "ListingInquiries");

            migrationBuilder.AlterColumn<Guid>(
                name: "AgentId",
                table: "ListingInquiries",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "BuyerEmail",
                table: "ListingInquiries",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerName",
                table: "ListingInquiries",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerPhone",
                table: "ListingInquiries",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OfferAmount",
                table: "ListingInquiries",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InquiryType",
                table: "ListingInquiries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE "ListingInquiries"
                SET "InquiryType" = CASE
                    WHEN "Message" ILIKE 'INSPECTION:%' THEN 1
                    WHEN "Message" ILIKE 'OFFER:%' THEN 2
                    WHEN "Message" ILIKE 'QUESTION:%' THEN 0
                    ELSE 3
                END
                WHERE "AgentId" IS NOT NULL;
                """);

            migrationBuilder.CreateTable(
                name: "ProductEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ListingId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductEvents_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductEvents_ListingId_Name_OccurredAt",
                table: "ProductEvents",
                columns: new[] { "ListingId", "Name", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductEvents_Name_OccurredAt",
                table: "ProductEvents",
                columns: new[] { "Name", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductEvents_OccurredAt_UserId",
                table: "ProductEvents",
                columns: new[] { "OccurredAt", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductEvents_UserId_OccurredAt",
                table: "ProductEvents",
                columns: new[] { "UserId", "OccurredAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_ListingInquiries_Agents_AgentId",
                table: "ListingInquiries",
                column: "AgentId",
                principalTable: "Agents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "GrowthFoundation is forward-only: rolling it back would delete "
                + "buyer inquiry fields and product-event history.");
        }
    }
}
