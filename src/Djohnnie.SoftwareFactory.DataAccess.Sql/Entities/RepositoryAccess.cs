using System.ComponentModel.DataAnnotations;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;

public sealed class RepositoryAccess
{
    [Key, MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    public DateTimeOffset LastAccessedAt { get; set; }
}
