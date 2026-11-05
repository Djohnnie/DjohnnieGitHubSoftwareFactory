using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Common;

/// <summary>The <c>owner/name</c> format of a GitHub repository; every request that puts a repository name in a URL or the database uses it.</summary>
public static class RepositoryNameRules
{
    /// <summary>An owner (letters, digits and hyphens) and a name (also dots and underscores) that is not "." or "..", so it can't leave the URL path it is put in.</summary>
    public const string Pattern = @"^[a-zA-Z0-9-]+/(?!\.\.?$)[a-zA-Z0-9_.-]+$";

    extension<T>(IRuleBuilder<T, string> rule)
    {
        public IRuleBuilderOptions<T, string> ValidRepositoryFullName()
        {
            return rule.NotEmpty().MaximumLength(200).Matches(Pattern);
        }
    }
}
