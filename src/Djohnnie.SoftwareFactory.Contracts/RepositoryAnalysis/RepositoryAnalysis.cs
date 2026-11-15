using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.Contracts.RepositoryAnalysis;

/// <summary>What the work items generated from a repository describe; chosen per run.</summary>
public enum RepositoryAnalysisMode
{
    /// <summary>A forward-looking backlog: gaps, technical debt, missing tests or docs, risks and unfinished work.</summary>
    [Description("Improvements")] Improvements,

    /// <summary>What the repository already does, as epics, features and user stories (e.g. to onboard an existing codebase).</summary>
    [Description("Describe what exists")] DescribeExisting
}

/// <summary>Starts a run that analyzes a repository and saves the work items it writes as drafts.</summary>
/// <param name="PathPrefix">Only analyze this folder (e.g. <c>src/Api</c>), or <c>null</c> for the whole repository.</param>
/// <param name="MaxFeatures">The most features the run plans; each one becomes a sub-task with its own model calls.</param>
/// <param name="RequestedBy">The GitHub login of the person who starts the run, if known.</param>
public sealed record StartRepositoryAnalysisRequest(
    string RepositoryFullName,
    RepositoryAnalysisMode Mode,
    [property: MaxLength(StartRepositoryAnalysisRequest.MaxPathPrefixLength)] string? PathPrefix,
    int MaxFeatures,
    string? RequestedBy)
{
    public const int MaxPathPrefixLength = 400;
    public const int DefaultMaxFeatures = 8;
    public const int MaxMaxFeatures = 25;
}

/// <summary>The result of discarding the drafts a run created.</summary>
/// <param name="Deleted">The drafts that were deleted.</param>
/// <param name="Kept">The items that were kept: accepted meanwhile, or parents of items that don't belong to the run.</param>
public sealed record DiscardedGeneratedDrafts(int Deleted, int Kept);
