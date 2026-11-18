using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.Assistant;
using Djohnnie.SoftwareFactory.Contracts.ProjectChats;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;

public sealed class ProjectChatStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider timeProvider) : IProjectChatStore
{
    public async Task<List<ProjectChatSummary>> GetAllAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await ChatsOf(db, repositoryFullName).AsNoTracking()
            .OrderByDescending(c => c.LastUsedAt)
            .ThenByDescending(c => c.SysId)
            .Select(c => new ProjectChatSummary(c.Id, c.Title, c.CreatedAt, c.LastUsedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProjectChat?> GetAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var chat = await ChatsOf(db, repositoryFullName).AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new ProjectChatSummary(c.Id, c.Title, c.CreatedAt, c.LastUsedAt))
            .SingleOrDefaultAsync(cancellationToken);
        if (chat is null)
        {
            return null;
        }

        var messages = await db.ProjectChatMessages.AsNoTracking()
            .Where(m => m.ChatId == id && m.DisplayText != null && !m.IsSummary)
            .OrderBy(m => m.Sequence)
            .Select(m => new ChatDisplayMessage(m.Role, m.DisplayText!, m.IsError))
            .ToListAsync(cancellationToken);
        return new ProjectChat(chat.Id, chat.Title, chat.CreatedAt, chat.LastUsedAt, messages);
    }

    public async Task<List<string>> GetPayloadsAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var messages = ChatsOf(db, repositoryFullName).AsNoTracking()
            .Where(c => c.Id == id)
            .SelectMany(c => c.Messages)
            .Where(m => m.Payload != null);
        var summary = await messages
            .Where(m => m.IsSummary)
            .OrderByDescending(m => m.Sequence)
            .Select(m => m.Payload!)
            .FirstOrDefaultAsync(cancellationToken);
        // By the flag rather than the position: messages added while the summary was written come after it but aren't in it.
        var rest = await messages
            .Where(m => !m.IsSummary && !m.IsSummarized)
            .OrderBy(m => m.Sequence)
            .Select(m => m.Payload!)
            .ToListAsync(cancellationToken);
        return summary is null ? rest : [summary, .. rest];
    }

    public async Task<ProjectChatHistoryToSummarize?> GetHistoryToSummarizeAsync(string repositoryFullName, Guid id, int minMessages, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var messages = ChatsOf(db, repositoryFullName).AsNoTracking()
            .Where(c => c.Id == id)
            .SelectMany(c => c.Messages);
        var pending = await messages
            .Where(m => !m.IsSummary && !m.IsSummarized)
            .OrderBy(m => m.Sequence)
            .Select(m => new { m.Sequence, m.Role, m.DisplayText, m.IsError })
            .ToListAsync(cancellationToken);
        var shown = pending.Where(m => m.DisplayText != null).ToList();
        if (shown.Count < minMessages)
        {
            return null;
        }

        var previous = await messages
            .Where(m => m.IsSummary)
            .OrderByDescending(m => m.Sequence)
            .Select(m => m.DisplayText)
            .FirstOrDefaultAsync(cancellationToken);
        return new ProjectChatHistoryToSummarize(previous,
            shown.Where(m => !m.IsError).Select(m => new ChatDisplayMessage(m.Role, m.DisplayText!, false)).ToList(),
            pending[^1].Sequence);
    }

    public async Task SaveSummaryAsync(string repositoryFullName, Guid id, string summary, string payload, int upToSequence, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var chat = await FindAsync(db, repositoryFullName, id, cancellationToken);
        var covered = await db.ProjectChatMessages
            .Where(m => m.ChatId == chat.Id && m.Sequence <= upToSequence && !m.IsSummarized)
            .ToListAsync(cancellationToken);
        foreach (var message in covered)
        {
            message.IsSummarized = true;
        }

        var last = await db.ProjectChatMessages.Where(m => m.ChatId == chat.Id).MaxAsync(m => (int?)m.Sequence, cancellationToken);
        chat.Messages.Add(new Entities.ProjectChatMessage
        {
            Sequence = last + 1 ?? 0,
            Role = ChatMessageRole.Assistant,
            DisplayText = summary,
            Payload = payload,
            IsSummary = true,
            CreatedAt = timeProvider.GetUtcNow()
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProjectChatSummary> StartAsync(string repositoryFullName, string title, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var chat = new Entities.ProjectChat
        {
            Id = Guid.NewGuid(),
            Repository = await db.GetOrAddRepositoryAsync(repositoryFullName, cancellationToken),
            Title = CleanTitle(title),
            CreatedAt = now,
            LastUsedAt = now
        };
        db.ProjectChats.Add(chat);
        await db.SaveChangesAsync(cancellationToken);
        return ToSummary(chat);
    }

    public async Task<ProjectChatSummary> AppendAsync(string repositoryFullName, Guid id, IReadOnlyList<StoredChatMessage> messages, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var chat = await FindAsync(db, repositoryFullName, id, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var last = await db.ProjectChatMessages.Where(m => m.ChatId == chat.Id).MaxAsync(m => (int?)m.Sequence, cancellationToken);
        var sequence = last + 1 ?? 0;
        foreach (var message in messages)
        {
            chat.Messages.Add(new Entities.ProjectChatMessage
            {
                Sequence = sequence++,
                Role = message.Role,
                DisplayText = message.DisplayText,
                Payload = message.Payload,
                IsError = message.IsError,
                CreatedAt = now
            });
        }

        chat.LastUsedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return ToSummary(chat);
    }

    public async Task<ProjectChatSummary> RenameAsync(string repositoryFullName, Guid id, string title, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var chat = await FindAsync(db, repositoryFullName, id, cancellationToken);
        chat.Title = CleanTitle(title);
        await db.SaveChangesAsync(cancellationToken);
        return ToSummary(chat);
    }

    public async Task DeleteAsync(string repositoryFullName, Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var chat = await ChatsOf(db, repositoryFullName).Include(c => c.Messages).SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (chat is not null)
        {
            db.ProjectChats.Remove(chat);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Matches through the repository, so a conversation can't be reached from another repository.</summary>
    private static IQueryable<Entities.ProjectChat> ChatsOf(AppDbContext db, string repositoryFullName)
    {
        var normalized = Entities.Repository.Normalize(repositoryFullName);
        return db.ProjectChats.Where(c => c.Repository.FullName == normalized);
    }

    private static async Task<Entities.ProjectChat> FindAsync(AppDbContext db, string repositoryFullName, Guid id, CancellationToken cancellationToken)
    {
        return await ChatsOf(db, repositoryFullName).SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new DomainRuleException("The conversation no longer exists.");
    }

    /// <summary>One line without repeated whitespace, cut off at the maximum length.</summary>
    internal static string CleanTitle(string? title)
    {
        var clean = string.Join(' ', (title ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0)
        {
            return ProjectChat.DefaultTitle;
        }

        return clean.Length <= ProjectChat.MaxTitleLength ? clean : clean[..(ProjectChat.MaxTitleLength - 1)].TrimEnd() + "…";
    }

    private static ProjectChatSummary ToSummary(Entities.ProjectChat chat)
    {
        return new(chat.Id, chat.Title, chat.CreatedAt, chat.LastUsedAt);
    }
}
