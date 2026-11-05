using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

/// <summary>Asks the Model Selector which model fits the (unsaved) item; nothing is stored.</summary>
public sealed record SelectWorkItemModelQuery(SelectWorkItemModelRequest Dto) : IRequest<Response<ModelSelectionResult>>;
