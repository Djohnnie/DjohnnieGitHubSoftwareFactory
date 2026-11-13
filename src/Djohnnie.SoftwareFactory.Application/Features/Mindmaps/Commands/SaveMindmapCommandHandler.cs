using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

public sealed class SaveMindmapCommandHandler(IMindmapStore store, IWorkItemStore items) : IRequestHandler<SaveMindmapCommand, Response<Mindmap>>
{
    public async Task<Response<Mindmap>> Handle(SaveMindmapCommand request, CancellationToken cancellationToken)
    {
        var mindmap = request.Dto.Mindmap.Copy();
        foreach (var node in mindmap.Nodes)
        {
            node.Text = node.Text.Trim();
            node.X = Math.Round(node.X, 1);
            node.Y = Math.Round(node.Y, 1);
        }

        // Ideas of closed work items are done, also when the item was closed while the map was being edited.
        if (mindmap.Nodes.Any(node => node.WorkItemId is not null && !node.IsDone))
        {
            var closed = (await items.GetAllAsync(mindmap.RepositoryFullName, cancellationToken: cancellationToken))
                .Where(item => item.Status == WorkItemStatus.Done).Select(item => item.Id).ToHashSet();
            foreach (var node in mindmap.Nodes.Where(node => node.WorkItemId is { } id && closed.Contains(id)))
            {
                node.IsDone = true;
            }
        }
        return new(await store.SaveAsync(mindmap, cancellationToken));
    }
}
