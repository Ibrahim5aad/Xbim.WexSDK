using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Octopus.Server.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddIfcProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IfcElements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityLabel = table.Column<int>(type: "INTEGER", nullable: false),
                    GlobalId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    TypeName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ObjectType = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    TypeObjectName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    TypeObjectType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ExtractedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IfcElements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IfcElements_ModelVersions_ModelVersionId",
                        column: x => x.ModelVersionId,
                        principalTable: "ModelVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IfcPropertySets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ElementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    GlobalId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IsTypePropertySet = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IfcPropertySets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IfcPropertySets_IfcElements_ElementId",
                        column: x => x.ElementId,
                        principalTable: "IfcElements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IfcQuantitySets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ElementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    GlobalId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IfcQuantitySets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IfcQuantitySets_IfcElements_ElementId",
                        column: x => x.ElementId,
                        principalTable: "IfcElements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IfcProperties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PropertySetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ValueType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IfcProperties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IfcProperties_IfcPropertySets_PropertySetId",
                        column: x => x.PropertySetId,
                        principalTable: "IfcPropertySets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IfcQuantities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuantitySetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Value = table.Column<double>(type: "REAL", nullable: true),
                    ValueType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IfcQuantities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IfcQuantities_IfcQuantitySets_QuantitySetId",
                        column: x => x.QuantitySetId,
                        principalTable: "IfcQuantitySets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IfcElements_ModelVersionId",
                table: "IfcElements",
                column: "ModelVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_IfcElements_ModelVersionId_EntityLabel",
                table: "IfcElements",
                columns: new[] { "ModelVersionId", "EntityLabel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IfcElements_ModelVersionId_GlobalId",
                table: "IfcElements",
                columns: new[] { "ModelVersionId", "GlobalId" });

            migrationBuilder.CreateIndex(
                name: "IX_IfcElements_ModelVersionId_TypeName",
                table: "IfcElements",
                columns: new[] { "ModelVersionId", "TypeName" });

            migrationBuilder.CreateIndex(
                name: "IX_IfcProperties_PropertySetId",
                table: "IfcProperties",
                column: "PropertySetId");

            migrationBuilder.CreateIndex(
                name: "IX_IfcProperties_PropertySetId_Name",
                table: "IfcProperties",
                columns: new[] { "PropertySetId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_IfcPropertySets_ElementId",
                table: "IfcPropertySets",
                column: "ElementId");

            migrationBuilder.CreateIndex(
                name: "IX_IfcPropertySets_ElementId_Name",
                table: "IfcPropertySets",
                columns: new[] { "ElementId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_IfcQuantities_QuantitySetId",
                table: "IfcQuantities",
                column: "QuantitySetId");

            migrationBuilder.CreateIndex(
                name: "IX_IfcQuantities_QuantitySetId_Name",
                table: "IfcQuantities",
                columns: new[] { "QuantitySetId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_IfcQuantitySets_ElementId",
                table: "IfcQuantitySets",
                column: "ElementId");

            migrationBuilder.CreateIndex(
                name: "IX_IfcQuantitySets_ElementId_Name",
                table: "IfcQuantitySets",
                columns: new[] { "ElementId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IfcProperties");

            migrationBuilder.DropTable(
                name: "IfcQuantities");

            migrationBuilder.DropTable(
                name: "IfcPropertySets");

            migrationBuilder.DropTable(
                name: "IfcQuantitySets");

            migrationBuilder.DropTable(
                name: "IfcElements");
        }
    }
}
