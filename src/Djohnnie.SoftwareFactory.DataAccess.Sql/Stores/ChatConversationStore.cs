using System.Linq.Expressions;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.Assistant;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;
using ChatMessage = Djohnnie.SoftwareFactory.DataAccess.Sql.Entities.ChatMessage;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public sealed class ChatConversationStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider) : IChatConversationStore
{
    public async Task<List<ChatDisplayMessage>> GetDisplayMessagesAsync(ChatOwner owner, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await OpenMessages(db, owner)
            .Where(m => m.DisplayText != null)
            .OrderBy(m => m.Sequence)
            .Select(m => new ChatDisplayMessage(m.Role, m.DisplayText!, m.IsError))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<string>> GetPayloadsAsync(ChatOwner owner, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await OpenMessages(db, owner)
            .Where(m => m.Payload != null)
            .OrderBy(m => m.Sequence)
            .Select(m => m.Payload!)
            .ToListAsync(cancellationToken);
    }

    public async Task AppendAsync(ChatOwner owner, string copilotKey, IReadOnlyList<StoredChatMessage> messages, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var conversation = await db.ChatConversations.SingleOrDefaultAsync(IsOpenConversationOf(owner), cancellationToken)
            ?? await StartConversationAsync(db, owner, copilotKey, now, cancellationToken);

        var last = await db.ChatMessages.Where(m => m.ConversationId == conversation.Id).MaxAsync(m => (int?)m.Sequence, cancellationToken);
        var sequence = last + 1 ?? 0;
        foreach (var message in messages)
        {
            conversation.Messages.Add(new ChatMessage
            {
                Sequence = sequence++,
                Role = message.Role,
                DisplayText = message.DisplayText,
                Payload = message.Payload,
                IsError = message.IsError,
                CreatedAt = now
            });
        }

        conversation.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CloseAsync(ChatOwner owner, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var conversation = await db.ChatConversations.SingleOrDefaultAsync(IsOpenConversationOf(owner), cancellationToken);
        if (conversation is not null)
        {
            conversation.ClosedAt = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static IQueryable<ChatMessage> OpenMessages(AppDbContext db, ChatOwner owner)
    {
        return db.ChatConversations.AsNoTracking().Where(IsOpenConversationOf(owner)).SelectMany(c => c.Messages);
    }

    /// <summary>Matches through the owner's repository, so a conversation can't be read from another repository.</summary>
    private static Expression<Func<ChatConversation, bool>> IsOpenConversationOf(ChatOwner owner)
    {
        var repository = NormalizedRepository(owner);
        var id = owner.Id;
        var fileName = owner.FileName;
        return owner.Kind switch
        {
            ChatOwnerKind.WorkItem => c => c.ClosedAt == null && c.WorkItemId == id && c.WorkItem!.Repository.FullName == repository,
            ChatOwnerKind.WorkItemTemplate => c => c.ClosedAt == null && c.WorkItemTemplateId == id,
            ChatOwnerKind.AgentTemplate => c => c.ClosedAt == null && c.AgentTemplateId == id,
            ChatOwnerKind.Agent => c => c.ClosedAt == null && c.Agent!.FileName == fileName && c.Agent.Repository.FullName == repository,
            ChatOwnerKind.CopilotInstructions => c => c.ClosedAt == null && c.Repository!.FullName == repository,
            _ => throw new ArgumentOutOfRangeException(nameof(owner))
        };
    }

    private static string? NormalizedRepository(ChatOwner owner)
    {
        return owner.RepositoryFullName is { } name ? Repository.Normalize(name) : null;
    }

    private static async Task<ChatConversation> StartConversationAsync(AppDbContext db, ChatOwner owner, string copilotKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var conversation = new ChatConversation { CopilotKey = copilotKey, CreatedAt = now, UpdatedAt = now };
        switch (owner.Kind)
        {
            case ChatOwnerKind.WorkItem:
                var repository = NormalizedRepository(owner);
                conversation.WorkItemId = await db.WorkItems.AnyAsync(w => w.Id == owner.Id && w.Repository.FullName == repository, cancellationToken)
                    ? owner.Id : throw new DomainRuleException("The work item no longer exists.");
                break;
            case ChatOwnerKind.WorkItemTemplate:
                conversation.WorkItemTemplateId = await db.WorkItemTemplates.AnyAsync(t => t.Id == owner.Id, cancellationToken)
                    ? owner.Id : throw new DomainRuleException("The template no longer exists.");
                break;
            case ChatOwnerKind.AgentTemplate:
                conversation.AgentTemplateId = await db.AgentTemplates.AnyAsync(t => t.Id == owner.Id, cancellationToken)
                    ? owner.Id : throw new DomainRuleException("The template no longer exists.");
                break;
            case ChatOwnerKind.Agent:
                conversation.AgentId = (await GetOrAddAgentAsync(db, owner, cancellationToken)).Id;
                break;
            case ChatOwnerKind.CopilotInstructions:
                conversation.RepositoryId = (await db.GetOrAddRepositoryAsync(owner.RepositoryFullName!, cancellationToken)).Id;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(owner));
        }

        db.ChatConversations.Add(conversation);
        return conversation;
    }

    /// <summary>The agent's row may not exist yet: rows are only added when the agents list is loaded.</summary>
    private static async Task<Agent> GetOrAddAgentAsync(AppDbContext db, ChatOwner owner, CancellationToken cancellationToken)
    {
        var repository = await db.GetOrAddRepositoryAsync(owner.RepositoryFullName!, cancellationToken);
        var agent = await db.Agents.SingleOrDefaultAsync(a => a.RepositoryId == repository.Id && a.FileName == owner.FileName, cancellationToken);
        if (agent is null)
        {
            agent = new Agent { RepositoryId = repository.Id, FileName = owner.FileName! };
            db.Agents.Add(agent);
            await db.SaveChangesAsync(cancellationToken);
        }

        return agent;
    }
}
