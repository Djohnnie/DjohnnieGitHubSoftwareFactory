namespace Djohnnie.SoftwareFactory.DataAccess.Sql;

internal static class ContractMapping
{
    extension(Entities.WorkItemTemplate value)
    {
        public Djohnnie.SoftwareFactory.Contracts.WorkItemTemplates.WorkItemTemplate ToContract()
        {
            return new()
            {
                Id = value.Id,
                Type = value.Type,
                Name = value.Name,
                TitleTemplate = value.TitleTemplate,
                Description = value.Description,
                CreatedAt = value.CreatedAt,
                UpdatedAt = value.UpdatedAt
            };
        }
    }

    extension(Entities.WorkItem value)
    {
        /// <summary>Requires <see cref="Entities.WorkItem.Repository"/> (and <see cref="Entities.WorkItem.Parent"/>, if any) to be loaded.</summary>
        public Djohnnie.SoftwareFactory.Contracts.WorkItems.WorkItem ToContract()
        {
            return new()
            {
                Id = value.Id,
                RepositoryFullName = value.Repository.FullName,
                Type = value.Type,
                ParentId = value.ParentId,
                Parent = value.Parent?.ToReference(),
                Title = value.Title,
                Description = value.Description,
                Status = value.Status,
                CustomAgent = value.CustomAgent,
                Model = value.Model,
                BaseBranch = value.BaseBranch,
                CustomInstructions = value.CustomInstructions,
                IssueNumber = value.IssueNumber,
                IssueId = value.IssueId,
                IssueUrl = value.IssueUrl,
                CreatedAt = value.CreatedAt,
                UpdatedAt = value.UpdatedAt,
                AcceptedAt = value.AcceptedAt,
                AssignedAt = value.AssignedAt
            };
        }

        public Djohnnie.SoftwareFactory.Contracts.WorkItems.WorkItemReference ToReference()
        {
            return new(value.Id, value.Type, value.Title, value.Status, value.IssueNumber, value.IssueUrl);
        }
    }

    extension(Entities.AiModel value)
    {
        public Djohnnie.SoftwareFactory.Contracts.AiModels.AiModel ToContract()
        {
            return new()
            {
                Id = value.Id,
                Name = value.Name,
                ModelId = value.ModelId,
                Provider = value.Provider,
                ReleaseStatus = value.ReleaseStatus,
                Category = value.Category,
                Tier = value.Tier,
                Threshold = value.Threshold,
                InputPrice = value.InputPrice,
                CachedInputPrice = value.CachedInputPrice,
                CacheWritePrice = value.CacheWritePrice,
                OutputPrice = value.OutputPrice,
                IsEnabled = value.IsEnabled,
                IsFavorite = value.IsFavorite,
                SourceUrl = value.SourceUrl,
                UpdatedAt = value.UpdatedAt
            };
        }
    }

    extension(Djohnnie.SoftwareFactory.Contracts.AiModels.AiModel value)
    {
        public Entities.AiModel ToEntity()
        {
            return new()
            {
                Id = value.Id,
                Name = value.Name,
                ModelId = value.ModelId,
                Provider = value.Provider,
                ReleaseStatus = value.ReleaseStatus,
                Category = value.Category,
                Tier = value.Tier,
                Threshold = value.Threshold,
                InputPrice = value.InputPrice,
                CachedInputPrice = value.CachedInputPrice,
                CacheWritePrice = value.CacheWritePrice,
                OutputPrice = value.OutputPrice,
                IsEnabled = value.IsEnabled,
                IsFavorite = value.IsFavorite,
                SourceUrl = value.SourceUrl,
                UpdatedAt = value.UpdatedAt
            };
        }
    }

    extension(Entities.AgentTemplate value)
    {
        public Djohnnie.SoftwareFactory.Contracts.AgentTemplates.AgentTemplate ToContract()
        {
            return new()
            {
                Id = value.Id,
                FileName = value.FileName,
                Name = value.Name,
                Description = value.Description,
                Content = value.Content,
                CreatedAt = value.CreatedAt,
                UpdatedAt = value.UpdatedAt
            };
        }
    }

    extension(Djohnnie.SoftwareFactory.Contracts.AgentTemplates.AgentTemplate value)
    {
        public Entities.AgentTemplate ToEntity()
        {
            return new()
            {
                Id = value.Id,
                FileName = value.FileName,
                Name = value.Name,
                Description = value.Description,
                Content = value.Content,
                CreatedAt = value.CreatedAt,
                UpdatedAt = value.UpdatedAt
            };
        }
    }
}
