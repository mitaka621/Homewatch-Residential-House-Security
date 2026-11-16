using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homewatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParallelCopiesPerShareToSystemSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParallelCopiesPerShare",
                table: "SystemSettings",
                type: "int",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("8f4d2a1c-0b6e-4c3a-9d57-1f2e3a4b5c6d"),
                column: "ParallelCopiesPerShare",
                value: 5);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParallelCopiesPerShare",
                table: "SystemSettings");
        }
    }
}
