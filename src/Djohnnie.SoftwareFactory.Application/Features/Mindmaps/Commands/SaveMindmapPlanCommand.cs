using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

/// <summary>Saves work items proposed by the Mindmap Planner as drafts and links them to their ideas, all or none.</summary>
public sealed record SaveMindmapPlanCommand(SaveMindmapPlanRequest Dto) : IRequest<Response<CollectionResult<WorkItem>>>;
