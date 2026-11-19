namespace Djohnnie.SoftwareFactory.DataAccess.Sql;

internal static class ContractMapping
{
    extension(Entities.TodoTask value)
    {
        public Djohnnie.SoftwareFactory.Contracts.TodoTasks.TodoTask ToContract()
        {
            return new()
            {
                Id = value.Id,
                RootId = value.RootId,
                ParentId = value.ParentId,
                Kind = value.Kind,
                Status = value.Status,
                RepositoryFullName = value.RepositoryFullName,
                Title = value.Title,
                Payload = value.Payload,
                Result = value.Result,
                Error = value.Error,
                Attempts = value.Attempts,
                MaxAttempts = value.MaxAttempts,
                NotBefore = value.NotBefore,
                CreatedBy = value.CreatedBy,
                CreatedAt = value.CreatedAt,
                QueuedAt = value.QueuedAt,
                StartedAt = value.StartedAt,
                CompletedAt = value.CompletedAt
            };
        }
    }

    extension(Entities.AgentRun value)
    {
        public Djohnnie.SoftwareFactory.Contracts.AgentRuns.AgentRun ToContract()
        {
            return new()
            {
                Id = value.Id,
                WorkItemId = value.WorkItemId,
                RepositoryFullName = value.RepositoryFullName,
                Status = value.Status,
                Attempts = value.Attempts,
                MaxAttempts = value.MaxAttempts,
                RequestedModel = value.RequestedModel,
                Models = value.Models,
                Branch = value.Branch,
                PullRequestNumber = value.PullRequestNumber,
                PullRequestUrl = value.PullRequestUrl,
                Summary = value.Summary,
                Error = value.Error,
                NanoAiu = value.NanoAiu,
                UsageComplete = value.UsageComplete,
                PremiumRequests = value.PremiumRequests,
                InputTokens = value.InputTokens,
                OutputTokens = value.OutputTokens,
                CacheReadTokens = value.CacheReadTokens,
                CacheWriteTokens = value.CacheWriteTokens,
                CreatedAt = value.CreatedAt,
                QueuedAt = value.QueuedAt,
                StartedAt = value.StartedAt,
                CompletedAt = value.CompletedAt
            };
        }
    }

    extension(Entities.AgentRunAttempt value)
    {
        public Djohnnie.SoftwareFactory.Contracts.AgentRuns.AgentRunAttempt ToContract()
        {
            return new()
            {
                Id = value.Id,
                AgentRunId = value.AgentRunId,
                Attempt = value.Attempt,
                Status = value.Status,
                Models = value.Models,
                Branch = value.Branch,
                PullRequestNumber = value.PullRequestNumber,
                PullRequestUrl = value.PullRequestUrl,
                Summary = value.Summary,
                Error = value.Error,
                NanoAiu = value.NanoAiu,
                UsageComplete = value.UsageComplete,
                PremiumRequests = value.PremiumRequests,
                InputTokens = value.InputTokens,
                OutputTokens = value.OutputTokens,
                CacheReadTokens = value.CacheReadTokens,
                CacheWriteTokens = value.CacheWriteTokens,
                StartedAt = value.StartedAt,
                CompletedAt = value.CompletedAt
            };
        }
    }

    extension(Entities.SyncLogEvent value)
    {
        public Djohnnie.SoftwareFactory.Contracts.SyncLog.SyncLogEvent ToContract()
        {
            return new(value.Id, value.RunId, value.OccurredAt, value.LastOccurredAt, value.Iterations, value.Level, value.Kind,
                value.RepositoryFullName, value.IssueNumber, value.WorkItemId, value.Message, value.Detail);
        }
    }

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
                TechnicalDetails = value.TechnicalDetails,
                Status = value.Status,
                Executor = value.Executor,
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
                PullRequestBranch = value.PullRequestBranch,
                GeneratedByTaskId = value.GeneratedByTaskId
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
                UpdatedAt = value.UpdatedAt,
                Usage = new(value.UsageSessions, value.UsageNanoAiu, value.UsageRequests, value.UsageInputTokens, value.UsageOutputTokens,
                    value.UsageCacheReadTokens, value.UsageCacheWriteTokens, value.UsageReasoningTokens)
            };
        }
    }

    extension(Entities.AgentRunModelUsage value)
    {
        public Djohnnie.SoftwareFactory.Contracts.AiModels.AiModelUsage ToContract(Entities.AgentRun run, Djohnnie.SoftwareFactory.Contracts.WorkItems.WorkItemReference? workItem)
        {
            return new()
            {
                Id = value.Id,
                AgentRunId = value.AgentRunId,
                AiModelId = value.AiModelId,
                Attempt = value.Attempt,
                RepositoryFullName = run.RepositoryFullName,
                WorkItemId = run.WorkItemId,
                WorkItem = workItem,
                Model = value.Model,
                NanoAiu = value.NanoAiu,
                Complete = value.Complete,
                Requests = value.Requests,
                PremiumRequests = value.PremiumRequests,
                InputTokens = value.InputTokens,
                OutputTokens = value.OutputTokens,
                CacheReadTokens = value.CacheReadTokens,
                CacheWriteTokens = value.CacheWriteTokens,
                ReasoningTokens = value.ReasoningTokens,
                OccurredAt = value.OccurredAt
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

    extension(Entities.Skill value)
    {
        public Djohnnie.SoftwareFactory.Contracts.Skills.Skill ToContract()
        {
            return new()
            {
                Id = value.Id,
                Name = value.Name,
                Description = value.Description,
                License = value.License,
                Compatibility = value.Compatibility,
                AllowedTools = value.AllowedTools,
                Instructions = value.Instructions,
                CreatedAt = value.CreatedAt,
                UpdatedAt = value.UpdatedAt
            };
        }
    }
}
