using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Configuration;

internal static class AuthSessionConfiguration
{
    extension(ModelBuilder modelBuilder)
    {
        public ModelBuilder ConfigureAuthSession()
        {
            return modelBuilder.Entity<AuthSession>(session =>
            {
                session.HasIndex(s => s.Key).IsUnique();

                // Expired sessions are removed by expiry time.
                session.HasIndex(s => s.ExpiresAt);
            });
        }
    }
}
