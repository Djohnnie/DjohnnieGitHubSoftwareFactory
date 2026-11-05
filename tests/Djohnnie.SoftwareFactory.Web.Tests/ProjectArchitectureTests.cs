using System.Reflection;
using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.Web.Components.Pages;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class ProjectArchitectureTests
{
    [Fact]
    public void AssembliesFollowTheDependencyHierarchy()
    {
        var web = typeof(WorkItemEditor).Assembly;
        var application = typeof(WorkItemService).Assembly;
        var sql = typeof(AppDbContext).Assembly;
        var agents = typeof(AssistantAgentFactory).Assembly;
        var contracts = typeof(WorkItem).Assembly;

        AssertReferences(web, application, contracts);
        AssertDoesNotReference(web, sql, agents);
        Assert.DoesNotContain(web.GetReferencedAssemblies(), reference =>
            reference.Name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || reference.Name.StartsWith("Microsoft.Agents", StringComparison.Ordinal)
            || reference.Name == "Microsoft.Extensions.AI.Abstractions");
        AssertReferences(application, sql, agents, contracts);
        AssertDoesNotReference(application, web);
        AssertDoesNotReference(sql, web, application, agents);
        AssertDoesNotReference(agents, web, application, sql);
        AssertDoesNotReference(contracts, web, application, sql, agents);
    }

    [Fact]
    public void RelocatedMigrationsMatchTheExistingDatabaseModel()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ArchitectureChecks;Trusted_Connection=True").Options);

        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(
            ["20261029222630_InitialCreate", "20261029225348_AddEpics", "20261030124727_AddAgentTemplates",
                "20261102125044_AddRepositoryAccesses", "20261102133114_AddEpicTemplates",
                "20261105083736_AddRepositoriesAgentsAndFavorites", "20261105095015_AddWorkItemHierarchy",
                "20261105104758_AddInternalAgents",
                "20261105123925_AddWorkItemAcceptClaim", "20261105131621_AddWorkItemAcceptIssueSince",
                "20261105131859_AddAuthSessions"],
            db.Database.GetMigrations());
        Assert.All(db.Model.GetEntityTypes(), entity =>
        {
            Assert.Equal(typeof(AppDbContext).Assembly, entity.ClrType.Assembly);
            Assert.NotEqual(typeof(WorkItem).Assembly, entity.ClrType.Assembly);
        });
    }

    [Fact]
    public void MediatorRequestsAndResponsesWrapContractRecords()
    {
        var requests = typeof(WorkItemService).Assembly.GetTypes()
            .Select(type => new
            {
                Type = type,
                Request = type.GetInterfaces().SingleOrDefault(contract =>
                    contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IRequest<>))
            })
            .Where(item => item.Request is not null).ToList();

        Assert.NotEmpty(requests);
        foreach (var request in requests)
        {
            var input = request.Type.GetProperty("Dto")!.PropertyType;
            var response = request.Request!.GenericTypeArguments[0];
            var output = response.GetProperty("Dto")!.PropertyType;

            Assert.Equal(typeof(WorkItem).Assembly, input.Assembly);
            Assert.Equal(typeof(WorkItem).Assembly, output.Assembly);
            Assert.NotNull(input.GetMethod("<Clone>$"));
            Assert.NotNull(output.GetMethod("<Clone>$"));
        }
    }

    private static void AssertReferences(Assembly assembly, params Assembly[] expected)
    {
        foreach (var dependency in expected)
        {
            Assert.Contains(assembly.GetReferencedAssemblies(), reference => reference.Name == dependency.GetName().Name);
        }
    }

    private static void AssertDoesNotReference(Assembly assembly, params Assembly[] forbidden)
    {
        foreach (var dependency in forbidden)
        {
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name == dependency.GetName().Name);
        }
    }
}
