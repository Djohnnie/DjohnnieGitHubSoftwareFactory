using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

public sealed class DeleteKnowledgeFileCommandHandler(IKnowledgeBaseStore store, IKnowledgeFileStorage storage, ILogger<DeleteKnowledgeFileCommandHandler> logger)
    : IRequestHandler<DeleteKnowledgeFileCommand, Response<OperationResult>>
{
    public async Task<Response<OperationResult>> Handle(DeleteKnowledgeFileCommand request, CancellationToken cancellationToken)
    {
        // The row goes first: a blob left behind is only wasted space, a row without its blob would be a broken file.
        await store.DeleteFileAsync(request.Dto.Id, cancellationToken);
        if (storage.IsConfigured)
        {
            try
            {
                await storage.DeleteAsync(request.Dto.Id, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not delete the blob of knowledge base file {FileId}; the file is removed from the library.", request.Dto.Id);
            }
        }

        return new(new());
    }
}
