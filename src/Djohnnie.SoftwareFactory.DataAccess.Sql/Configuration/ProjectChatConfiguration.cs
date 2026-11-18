using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Configuration;

internal static class ProjectChatConfiguration
{
    extension(ModelBuilder modelBuilder)
    {
        /// <remarks>
        /// Like everything else that belongs to a repository, a conversation blocks deleting it; messages are deleted
        /// with their conversation.
        /// </remarks>
        public ModelBuilder ConfigureProjectChat()
        {
            return modelBuilder
                .Entity<ProjectChat>(chat =>
                {
                    chat.HasOne(c => c.Repository).WithMany()
                        .HasForeignKey(c => c.RepositoryId).OnDelete(DeleteBehavior.Restrict);
                    chat.HasIndex(c => new { c.RepositoryId, c.LastUsedAt });
                })
                .Entity<ProjectChatMessage>(message =>
                {
                    message.HasOne(m => m.Chat).WithMany(c => c.Messages)
                        .HasForeignKey(m => m.ChatId).OnDelete(DeleteBehavior.Cascade);
                    message.HasIndex(m => new { m.ChatId, m.Sequence }).IsUnique();
                });
        }
    }
}
