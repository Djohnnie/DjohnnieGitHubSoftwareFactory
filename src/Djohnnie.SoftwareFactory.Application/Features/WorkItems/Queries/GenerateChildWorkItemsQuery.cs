using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

/// <summary>Lets the Work Item Breakdown agent propose the children of an item; nothing is stored.</summary>
public sealed record GenerateChildWorkItemsQuery(GenerateChildWorkItemsRequest Dto) : IRequest<Response<GeneratedChildWorkItems>>;
