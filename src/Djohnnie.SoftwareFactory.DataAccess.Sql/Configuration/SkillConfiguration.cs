using Djohnnie.SoftwareFactory.DataAccess.Sql.Entities;
using Microsoft.EntityFrameworkCore;

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Configuration;

internal static class SkillConfiguration
{
    extension(ModelBuilder modelBuilder)
    {
        public ModelBuilder ConfigureSkill()
        {
            return modelBuilder.Entity<Skill>(skill =>
            {
                skill.HasIndex(s => s.Name).IsUnique();
            });
        }
    }
}
