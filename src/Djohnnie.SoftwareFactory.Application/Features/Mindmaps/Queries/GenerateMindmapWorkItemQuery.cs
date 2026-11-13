using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

/// <summary>Lets the Mindmap Work Item Writer write a new work item for a mindmap idea; nothing is stored.</summary>
public sealed record GenerateMindmapWorkItemQuery(GenerateMindmapWorkItemRequest Dto) : IRequest<Response<GeneratedWorkItem>>;
