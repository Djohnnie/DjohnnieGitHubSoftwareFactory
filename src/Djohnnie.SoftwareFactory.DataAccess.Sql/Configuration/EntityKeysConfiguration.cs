using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Configuration;

internal static class EntityKeysConfiguration
{
    extension(ModelBuilder modelBuilder)
    {
        /// <summary>
        /// Every entity has a nonclustered primary key on its <see cref="Entity.Id"/> and a clustered alternate key on
        /// its identity column <see cref="Entity.SysId"/>, which keeps random GUIDs from fragmenting the tables.
        /// </summary>
        public ModelBuilder ConfigureEntityKeys()
        {
            var entities = modelBuilder.Model.GetEntityTypes()
                .Select(entity => entity.ClrType)
                .Where(type => type.IsAssignableTo(typeof(Entity)))
                .ToList();

            foreach (var type in entities)
            {
                var entity = modelBuilder.Entity(type);
                entity.HasKey(nameof(Entity.Id)).IsClustered(false);
                entity.HasAlternateKey(nameof(Entity.SysId)).IsClustered();
                entity.Property<int>(nameof(Entity.SysId)).ValueGeneratedOnAdd().UseIdentityColumn();
            }

            return modelBuilder;
        }
    }
}
