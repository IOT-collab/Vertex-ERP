using Microsoft.EntityFrameworkCore;
using Shiva_Gautam.Models;

namespace Shiva_Gautam.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<AppUser> AppUsers => Set<AppUser>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<AppUser>(entity =>
            {
                entity.ToTable("Users");
                entity.HasIndex(user => user.NormalizedUsername).IsUnique();
                entity.Property(user => user.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            });
        }
    }
}
