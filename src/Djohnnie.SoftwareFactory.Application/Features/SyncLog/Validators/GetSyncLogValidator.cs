using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.SyncLog;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.SyncLog;

public sealed class GetSyncLogValidator : AbstractValidator<GetSyncLogQuery>
{
    public GetSyncLogValidator()
    {
        RuleFor(request => request.Dto.Page).GreaterThanOrEqualTo(0);
        RuleFor(request => request.Dto.PageSize).InclusiveBetween(1, SyncLogQuery.MaxPageSize);
        RuleFor(request => request.Dto.MinimumLevel).IsInEnum();
        RuleFor(request => request.Dto.RepositoryFullName!).ValidRepositoryFullName().When(request => request.Dto.RepositoryFullName is not null);
    }
}
