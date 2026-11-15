using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.RepositoryAnalysis;
using Djohnnie.SoftwareFactory.Contracts.TodoTasks;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.TodoTasks;

public sealed record StartRepositoryAnalysisCommand(StartRepositoryAnalysisRequest Dto) : IRequest<Response<TodoTask>>;
