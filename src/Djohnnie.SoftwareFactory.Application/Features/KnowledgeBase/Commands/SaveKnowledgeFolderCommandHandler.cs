using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

public sealed class SaveKnowledgeFolderCommandHandler(IKnowledgeBaseStore store, KnowledgeFileMetadataSync metadata)
    : IRequestHandler<SaveKnowledgeFolderCommand, Response<KnowledgeFolder>>
{
    public async Task<Response<KnowledgeFolder>> Handle(SaveKnowledgeFolderCommand request, CancellationToken cancellationToken)
    {
        var stored = await store.SaveFolderAsync(request.Dto.Folder, cancellationToken);
        if (request.Dto.Folder.Id != Guid.Empty)
        {
            // Renaming or moving a folder changes the path of every file below it, so their blob metadata follows.
            var library = await store.GetFullLibraryAsync(cancellationToken);
            var folders = library.SubtreeOf(stored.Id);
            var files = library.Files.Where(file => file.FolderId is { } folderId && folders.Contains(folderId)).ToList();
            await metadata.UpdateAsync(files, library, cancellationToken);
        }

        return new(stored);
    }
}
