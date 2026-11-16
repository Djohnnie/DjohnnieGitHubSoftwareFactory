using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

/// <summary>The item with the given id (a parent) and all items above it, closest first.</summary>
public sealed record GetWorkItemAncestorsQuery(RepositoryItemRequest Dto) : IRequest<Response<CollectionResult<WorkItem>>>;
