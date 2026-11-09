using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Configuration;

internal static class ChatConversationConfiguration
{
    extension(ModelBuilder modelBuilder)
    {
        /// <remarks>
        /// Conversations are deleted with their owner, except that a repository can't be deleted while one still
        /// references it, like everything else that belongs to a repository.
        /// </remarks>
        public ModelBuilder ConfigureChatConversation()
        {
            return modelBuilder
                .Entity<ChatConversation>(conversation =>
                {
                    conversation.ToTable(table => table.HasCheckConstraint("CK_ChatConversations_ExactlyOneOwner",
                        "(CASE WHEN [WorkItemId] IS NULL THEN 0 ELSE 1 END"
                        + " + CASE WHEN [WorkItemTemplateId] IS NULL THEN 0 ELSE 1 END"
                        + " + CASE WHEN [AgentTemplateId] IS NULL THEN 0 ELSE 1 END"
                        + " + CASE WHEN [AgentId] IS NULL THEN 0 ELSE 1 END"
                        + " + CASE WHEN [RepositoryId] IS NULL THEN 0 ELSE 1 END) = 1"));

                    conversation.HasOne(c => c.WorkItem).WithMany().HasForeignKey(c => c.WorkItemId).OnDelete(DeleteBehavior.Cascade);
                    conversation.HasOne(c => c.WorkItemTemplate).WithMany().HasForeignKey(c => c.WorkItemTemplateId).OnDelete(DeleteBehavior.Cascade);
                    conversation.HasOne(c => c.AgentTemplate).WithMany().HasForeignKey(c => c.AgentTemplateId).OnDelete(DeleteBehavior.Cascade);
                    conversation.HasOne(c => c.Agent).WithMany().HasForeignKey(c => c.AgentId).OnDelete(DeleteBehavior.Cascade);
                    conversation.HasOne(c => c.Repository).WithMany().HasForeignKey(c => c.RepositoryId).OnDelete(DeleteBehavior.Restrict);

                    // One open conversation per owner; closed ones are kept.
                    conversation.HasIndex(c => c.WorkItemId).IsUnique().HasFilter("[WorkItemId] IS NOT NULL AND [ClosedAt] IS NULL");
                    conversation.HasIndex(c => c.WorkItemTemplateId).IsUnique().HasFilter("[WorkItemTemplateId] IS NOT NULL AND [ClosedAt] IS NULL");
                    conversation.HasIndex(c => c.AgentTemplateId).IsUnique().HasFilter("[AgentTemplateId] IS NOT NULL AND [ClosedAt] IS NULL");
                    conversation.HasIndex(c => c.AgentId).IsUnique().HasFilter("[AgentId] IS NOT NULL AND [ClosedAt] IS NULL");
                    conversation.HasIndex(c => c.RepositoryId).IsUnique().HasFilter("[RepositoryId] IS NOT NULL AND [ClosedAt] IS NULL");
                })
                .Entity<ChatMessage>(message =>
                {
                    message.HasOne(m => m.Conversation).WithMany(c => c.Messages)
                        .HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
                    message.HasIndex(m => new { m.ConversationId, m.Sequence }).IsUnique();
                });
        }
    }
}
