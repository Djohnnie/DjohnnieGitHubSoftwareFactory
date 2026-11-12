namespace Djohnnie.SoftwareFactory.DataAccess.Sql;

internal static class ContractMapping
{
    extension(Entities.InternalAgent value)
    {
        /// <summary>Requires <see cref="Entities.InternalAgent.InternalModel"/> to be loaded.</summary>
        public Djohnnie.SoftwareFactory.Contracts.InternalAgents.InternalAgent ToContract()
        {
            return new()
            {
                Id = value.Id,
                Key = value.Key,
                Name = value.Name,
                Description = value.Description,
                Kind = value.Kind,
                Instructions = value.Instructions,
                ModelId = value.InternalModelId,
                Model = value.InternalModel?.Name ?? string.Empty,
                ModelLabel = value.InternalModel?.Label ?? string.Empty,
                IsEnabled = value.IsEnabled,
                CreatedAt = value.CreatedAt,
                UpdatedAt = value.UpdatedAt,
                RequestCount = value.RequestCount,
                TotalInputTokens = value.TotalInputTokens,
                TotalOutputTokens = value.TotalOutputTokens,
                TotalCachedInputTokens = value.TotalCachedInputTokens,
                TotalReasoningTokens = value.TotalReasoningTokens,
                TotalInputCost = value.TotalInputCost,
                TotalOutputCost = value.TotalOutputCost
            };
        }
    }

    extension(Entities.InternalAgentUsage value)
    {
        /// <summary>Requires <see cref="Entities.InternalAgentUsage.InternalAgent"/> to be loaded.</summary>
        public Djohnnie.SoftwareFactory.Contracts.InternalAgents.InternalAgentUsage ToContract()
        {
            return new(value.OccurredAt, value.InternalAgent.Key, value.InternalAgent.Name, value.Source, value.Model, value.InputTokens, value.OutputTokens, value.CachedInputTokens, value.ReasoningTokens,
                value.InputPricePerMillion, value.OutputPricePerMillion, value.CachedInputPricePerMillion, value.InputCost, value.OutputCost);
        }
    }

    extension(Entities.InternalModel value)
    {
        public Djohnnie.SoftwareFactory.Contracts.InternalModels.InternalModel ToContract()
        {
            return new()
            {
                Id = value.Id,
                Name = value.Name,
                Label = value.Label,
                Description = value.Description,
                InputPricePerMillion = value.InputPricePerMillion,
                OutputPricePerMillion = value.OutputPricePerMillion,
                CachedInputPricePerMillion = value.CachedInputPricePerMillion,
                IsFlagship = value.IsFlagship,
                IsDefault = value.IsDefault,
                CreatedAt = value.CreatedAt,
                UpdatedAt = value.UpdatedAt
            };
        }
    }

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
                IsDefault = value.IsDefault,
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
                AssignedAt = value.AssignedAt,
                Progress = value.Progress,
                PullRequestNumber = value.PullRequestNumber,
                PullRequestUrl = value.PullRequestUrl,
                PullRequestChecks = value.PullRequestChecks,
                AgentRunUrl = value.AgentRunUrl,
                PullRequestBranch = value.PullRequestBranch
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
