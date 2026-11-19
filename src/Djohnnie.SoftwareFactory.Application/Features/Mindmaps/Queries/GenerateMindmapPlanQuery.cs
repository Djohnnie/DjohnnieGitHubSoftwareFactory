using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

/// <summary>Lets the Mindmap Planner propose an epic with child work items for a whole mindmap; nothing is stored.</summary>
public sealed record GenerateMindmapPlanQuery(GenerateMindmapPlanRequest Dto) : IRequest<Response<GeneratedMindmapPlan>>;
