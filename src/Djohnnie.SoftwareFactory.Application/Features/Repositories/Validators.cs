using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

public static class RepositoryNameRules
{
    public const string Pattern = @"^[a-zA-Z0-9_.-]+/[a-zA-Z0-9_.-]+$";

    public static IRuleBuilderOptions<T, string> ValidRepositoryFullName<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(200).Matches(Pattern);
}

public sealed class RecordRepositoryAccessValidator : AbstractValidator<RecordRepositoryAccessCommand>
{
    public RecordRepositoryAccessValidator() =>
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
}

public sealed class SetRepositoryFavoriteValidator : AbstractValidator<SetRepositoryFavoriteCommand>
{
    public SetRepositoryFavoriteValidator() =>
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
}
