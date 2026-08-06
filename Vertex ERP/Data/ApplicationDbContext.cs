using Microsoft.EntityFrameworkCore;
using VertexERP.Models;

namespace VertexERP.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<AppUser> AppUsers => Set<AppUser>();
        public DbSet<Employee> Employees => Set<Employee>();
        public DbSet<Department> Departments => Set<Department>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.HasSequence<long>("EmployeeCodeSequence");

            modelBuilder.Entity<AppUser>(entity =>
            {
                entity.ToTable("Users");
                entity.HasIndex(user => user.NormalizedUsername).IsUnique();
                entity.Property(user => user.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            modelBuilder.Entity<Employee>(entity =>
            {
                entity.ToTable("Employees");
                entity.HasIndex(employee => employee.EmployeeCode).IsUnique();
                entity.HasIndex(employee => employee.Email).IsUnique();
                entity.HasIndex(employee => employee.PhoneNumber).IsUnique();
                entity.Property(employee => employee.CreatedDate).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(employee => employee.ReportingManager)
                    .WithMany(manager => manager.DirectReports)
                    .HasForeignKey(employee => employee.ReportingManagerId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Department>(entity =>
            {
                entity.ToTable("Departments");
                entity.HasIndex(department => department.DepartmentName).IsUnique();
                entity.HasIndex(department => department.DepartmentCode).IsUnique();
                entity.Property(department => department.CreatedDate).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasMany(department => department.Employees)
                    .WithOne(employee => employee.DepartmentEntity)
                    .HasForeignKey(employee => employee.DepartmentId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(department => department.Manager)
                    .WithMany()
                    .HasForeignKey(department => department.ManagerId)
                    .OnDelete(DeleteBehavior.SetNull);
            });
        }
    }
}
