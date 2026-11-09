using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using AiModelContract = Djohnnie.SoftwareFactory.Contracts.AiModels.AiModel;

namespace Djohnnie.SoftwareFactory.Web.Tests;

/// <summary>The key structure of the tables, which only a real SQL Server shows (see <see cref="SqlServerDatabase"/>).</summary>
public class SqlServerMigrationTests
{
    private const string LastMigrationWithIntegerKeys = "20261109105650_AddWorkItemTemplateDefault";

    private const string SeedIntegerKeyData = """
        INSERT INTO [Repositories] ([FullName], [Name], [IsFavorite]) VALUES ('owner/first', 'first', 0), ('owner/second', 'second', 1);
        INSERT INTO [WorkItems] ([RepositoryId], [Type], [ParentId], [Title], [Description], [Status], [CreatedAt], [UpdatedAt])
            VALUES (2, 0, NULL, 'Epic', 'D', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
        INSERT INTO [WorkItems] ([RepositoryId], [Type], [ParentId], [Title], [Description], [Status], [CreatedAt], [UpdatedAt])
            VALUES (2, 1, 1, 'Feature', 'D', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
        INSERT INTO [Agents] ([RepositoryId], [FileName], [IsFavorite]) VALUES (2, 'reviewer', 1);
        INSERT INTO [AuthSessions] ([Key], [Ticket], [ExpiresAt]) VALUES ('session-key', 0x0102, SYSDATETIMEOFFSET());
        INSERT INTO [ChatConversations] ([CopilotKey], [WorkItemId], [CreatedAt], [UpdatedAt]) VALUES ('copilot', 2, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
        INSERT INTO [ChatConversations] ([CopilotKey], [RepositoryId], [CreatedAt], [UpdatedAt]) VALUES ('instructions', 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
        INSERT INTO [ChatMessages] ([ConversationId], [Sequence], [Role], [DisplayText], [IsError], [CreatedAt])
            VALUES (1, 0, 0, 'Hello', 0, SYSDATETIMEOFFSET()), (1, 1, 1, 'Hi', 0, SYSDATETIMEOFFSET()), (2, 0, 0, 'Other', 0, SYSDATETIMEOFFSET());
        """;

    /// <summary>Tables missing the required structure, with what is wrong; empty when all of them are right.</summary>
    private const string WrongKeyStructure = """
        SELECT [t].[name] + ': ' + [p].[Problem] AS [Value]
        FROM [sys].[tables] AS [t]
        CROSS APPLY (
            SELECT 'SysId is not an int identity' AS [Problem]
            WHERE NOT EXISTS (SELECT 1 FROM [sys].[identity_columns] AS [c]
                WHERE [c].[object_id] = [t].[object_id] AND [c].[name] = 'SysId' AND TYPE_NAME([c].[system_type_id]) = 'int')
            UNION ALL SELECT 'SysId is not the unique clustered key'
            WHERE NOT EXISTS (SELECT 1 FROM [sys].[indexes] AS [i]
                JOIN [sys].[index_columns] AS [ic] ON [ic].[object_id] = [i].[object_id] AND [ic].[index_id] = [i].[index_id]
                JOIN [sys].[columns] AS [c] ON [c].[object_id] = [ic].[object_id] AND [c].[column_id] = [ic].[column_id]
                WHERE [i].[object_id] = [t].[object_id] AND [i].[type_desc] = 'CLUSTERED' AND [i].[is_unique_constraint] = 1 AND [c].[name] = 'SysId')
            UNION ALL SELECT 'Id is not the nonclustered primary key'
            WHERE NOT EXISTS (SELECT 1 FROM [sys].[indexes] AS [i]
                JOIN [sys].[index_columns] AS [ic] ON [ic].[object_id] = [i].[object_id] AND [ic].[index_id] = [i].[index_id]
                JOIN [sys].[columns] AS [c] ON [c].[object_id] = [ic].[object_id] AND [c].[column_id] = [ic].[column_id]
                WHERE [i].[object_id] = [t].[object_id] AND [i].[type_desc] = 'NONCLUSTERED' AND [i].[is_primary_key] = 1
                    AND [c].[name] = 'Id' AND TYPE_NAME([c].[system_type_id]) = 'uniqueidentifier')
            UNION ALL SELECT 'Id has a default value'
            WHERE EXISTS (SELECT 1 FROM [sys].[default_constraints] AS [d]
                JOIN [sys].[columns] AS [c] ON [c].[object_id] = [d].[parent_object_id] AND [c].[column_id] = [d].[parent_column_id]
                WHERE [d].[parent_object_id] = [t].[object_id] AND [c].[name] = 'Id')
        ) AS [p]
        WHERE [t].[name] <> '__EFMigrationsHistory'
        """;

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryTable_HasANonclusteredGuidKeyAndAClusteredIdentitySysId()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using var db = database.Factory.CreateDbContext();

        Assert.Empty(await db.Database.SqlQueryRaw<string>(WrongKeyStructure).ToListAsync(_cancellationToken));
    }

    [Fact]
    public async Task MigratingToGuidKeys_KeepsTheDataAndItsRelations()
    {
        await using var database = await SqlServerDatabase.CreateAsync(LastMigrationWithIntegerKeys);
        await using var db = database.Factory.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync(SeedIntegerKeyData, _cancellationToken);

        await db.GetService<IMigrator>().MigrateAsync(cancellationToken: _cancellationToken);

        Assert.Empty(await db.Database.SqlQueryRaw<string>(WrongKeyStructure).ToListAsync(_cancellationToken));

        var feature = await db.WorkItems.AsNoTracking().Include(e => e.Parent).Include(e => e.Repository)
            .SingleAsync(e => e.Title == "Feature", _cancellationToken);
        Assert.Equal((2, "Epic", 1, "owner/second"), (feature.SysId, feature.Parent!.Title, feature.Parent.SysId, feature.Repository.FullName));
        Assert.NotEqual(Guid.Empty, feature.Id);

        var agent = await db.Agents.AsNoTracking().Include(a => a.Repository).SingleAsync(_cancellationToken);
        Assert.Equal("owner/second", agent.Repository.FullName);

        var session = await db.AuthSessions.AsNoTracking().SingleAsync(_cancellationToken);
        Assert.Equal("session-key", session.Key);
        Assert.Equal([1, 2], session.Ticket);

        var conversations = await db.ChatConversations.AsNoTracking().Include(c => c.WorkItem).Include(c => c.Repository).Include(c => c.Messages)
            .OrderBy(c => c.SysId).ToListAsync(_cancellationToken);
        Assert.Equal("Feature", conversations[0].WorkItem!.Title);
        Assert.Equal(["Hello", "Hi"], conversations[0].Messages.OrderBy(m => m.Sequence).Select(m => m.DisplayText));
        Assert.Equal("owner/first", conversations[1].Repository!.FullName);
        Assert.Equal("Other", Assert.Single(conversations[1].Messages).DisplayText);

        // The identity carries on after the highest kept value.
        var repository = Repository.Create("owner/third");
        db.Repositories.Add(repository);
        await db.SaveChangesAsync(_cancellationToken);
        Assert.Equal(3, repository.SysId);
    }

    [Fact]
    public async Task UpdatingADetachedEntity_LeavesItsSysIdAlone()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var store = new AiModelStore(database.Factory, TimeProvider.System);
        await store.SaveAsync(new AiModelContract { Name = "First", ModelId = "first" }, _cancellationToken);
        var model = await store.SaveAsync(new AiModelContract { Name = "GPT", ModelId = "gpt" }, _cancellationToken);

        model.ModelId = "gpt-renamed";
        await store.SaveAsync(model, _cancellationToken);

        await using var db = database.Factory.CreateDbContext();
        var stored = await db.AiModels.AsNoTracking().SingleAsync(m => m.Id == model.Id, _cancellationToken);
        Assert.Equal(("gpt-renamed", 2), (stored.ModelId, stored.SysId));
    }
}
