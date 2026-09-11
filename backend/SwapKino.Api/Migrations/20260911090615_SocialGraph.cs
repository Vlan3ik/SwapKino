using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwapKino.Api.Migrations;

public partial class SocialGraph : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("Follows", table => new
        {
            FollowerId = table.Column<Guid>("uuid", nullable: false),
            FollowingId = table.Column<Guid>("uuid", nullable: false),
            CreatedAt = table.Column<DateTime>("timestamp with time zone", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_Follows", x => new { x.FollowerId, x.FollowingId });
            table.ForeignKey("FK_Follows_AspNetUsers_FollowerId", x => x.FollowerId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_Follows_AspNetUsers_FollowingId", x => x.FollowingId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
        });
        m.CreateIndex("IX_Follows_FollowingId", "Follows", "FollowingId");

        m.CreateTable("MovieComments", table => new
        {
            Id = table.Column<Guid>("uuid", nullable: false),
            AuthorId = table.Column<Guid>("uuid", nullable: false),
            TmdbId = table.Column<int>("integer", nullable: false),
            IsSeries = table.Column<bool>("boolean", nullable: false),
            ParentCommentId = table.Column<Guid>("uuid", nullable: true),
            Text = table.Column<string>("text", nullable: false),
            CreatedAt = table.Column<DateTime>("timestamp with time zone", nullable: false),
            DeletedAt = table.Column<DateTime>("timestamp with time zone", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_MovieComments", x => x.Id);
            table.ForeignKey("FK_MovieComments_AspNetUsers_AuthorId", x => x.AuthorId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_MovieComments_MovieComments_ParentCommentId", x => x.ParentCommentId, "MovieComments", "Id", onDelete: ReferentialAction.SetNull);
        });
        m.CreateIndex("IX_MovieComments_AuthorId", "MovieComments", "AuthorId");
        m.CreateIndex("IX_MovieComments_ParentCommentId", "MovieComments", "ParentCommentId");
        m.CreateIndex("IX_MovieComments_TmdbId_IsSeries_CreatedAt", "MovieComments", new[] { "TmdbId", "IsSeries", "CreatedAt" });

        m.CreateTable("CommentReactions", table => new
        {
            CommentId = table.Column<Guid>("uuid", nullable: false),
            UserId = table.Column<Guid>("uuid", nullable: false),
            Type = table.Column<string>("text", nullable: false),
            CreatedAt = table.Column<DateTime>("timestamp with time zone", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_CommentReactions", x => new { x.CommentId, x.UserId });
            table.ForeignKey("FK_CommentReactions_MovieComments_CommentId", x => x.CommentId, "MovieComments", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_CommentReactions_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
        });
        m.CreateIndex("IX_CommentReactions_UserId", "CommentReactions", "UserId");
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("CommentReactions");
        m.DropTable("MovieComments");
        m.DropTable("Follows");
    }
}
