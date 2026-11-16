using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

public sealed class UploadKnowledgeFileCommandHandler(IKnowledgeBaseStore store, IKnowledgeFileStorage storage, ILogger<UploadKnowledgeFileCommandHandler> logger)
    : IRequestHandler<UploadKnowledgeFileCommand, Response<KnowledgeFile>>
{
    public const string DefaultContentType = "application/octet-stream";

    public async Task<Response<KnowledgeFile>> Handle(UploadKnowledgeFileCommand request, CancellationToken cancellationToken)
    {
        if (!storage.IsConfigured)
        {
            throw new DomainRuleException("The knowledge base file storage is not configured.");
        }

        var dto = request.Dto;
        var file = new KnowledgeFile
        {
            // The blob is named after the id, so it's chosen here, before anything is stored.
            Id = Guid.NewGuid(),
            FolderId = dto.FolderId,
            RepositoryFullName = dto.RepositoryFullName?.Trim().ToLowerInvariant(),
            Title = dto.Title,
            Description = dto.Description ?? string.Empty,
            FileName = KnowledgeFileNames.Clean(dto.FileName),
            ContentType = string.IsNullOrWhiteSpace(dto.ContentType) ? DefaultContentType : dto.ContentType.Trim(),
            Size = dto.Size
        };

        // The folder's path goes into the blob metadata; a missing folder is refused when the row is added below.
        var library = await store.GetFullLibraryAsync(cancellationToken);
        await storage.StoreAsync(file.Id, dto.Content, file.FileName, file.ContentType, KnowledgeFileMetadata.For(file, library), cancellationToken);
        try
        {
            var stored = await store.AddFileAsync(file, cancellationToken);
            return new(stored with { Url = storage.GetUrl(stored.Id) });
        }
        catch
        {
            // Without its row nobody can find or delete the blob, so remove it again.
            await DeleteOrphanAsync(file.Id);
            throw;
        }
    }

    private async Task DeleteOrphanAsync(Guid id)
    {
        try
        {
            await storage.DeleteAsync(id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete the blob of knowledge base file {FileId} after saving it failed.", id);
        }
    }
}
