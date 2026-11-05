using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

public static class RepositoryNameRules
{
    public const string Pattern = @"^[a-zA-Z0-9_.-]+/[a-zA-Z0-9_.-]+$";

    extension<T>(IRuleBuilder<T, string> rule)
    {
        public IRuleBuilderOptions<T, string> ValidRepositoryFullName()
        {
            return rule.NotEmpty().MaximumLength(200).Matches(Pattern);
        }
    }
}
