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
        public DbSet<BiometricDevice> BiometricDevices => Set<BiometricDevice>();
        public DbSet<AttendanceLog> AttendanceLogs => Set<AttendanceLog>();
        public DbSet<EmployeeDeviceMapping> EmployeeDeviceMappings => Set<EmployeeDeviceMapping>();
        public DbSet<WorkTask> WorkTasks => Set<WorkTask>();

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

            modelBuilder.Entity<BiometricDevice>(entity =>
            {
                entity.ToTable("BiometricDevices");
                entity.HasIndex(device => device.SerialNumber).IsUnique();
                entity.Property(device => device.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            modelBuilder.Entity<EmployeeDeviceMapping>(entity =>
            {
                entity.ToTable("EmployeeDeviceMapping");
                entity.HasIndex(mapping => new { mapping.BiometricDeviceId, mapping.DeviceUserId }).IsUnique();
                entity.HasIndex(mapping => new { mapping.BiometricDeviceId, mapping.EmployeeId }).IsUnique();
                entity.HasOne(mapping => mapping.BiometricDevice)
                    .WithMany(device => device.EmployeeMappings)
                    .HasForeignKey(mapping => mapping.BiometricDeviceId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(mapping => mapping.Employee)
                    .WithMany()
                    .HasForeignKey(mapping => mapping.EmployeeId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<AttendanceLog>(entity =>
            {
                entity.ToTable("AttendanceLogs");
                entity.HasIndex(log => log.UniqueHash).IsUnique();
                entity.HasIndex(log => new { log.BiometricDeviceId, log.DeviceUserId, log.PunchTime });
                entity.HasIndex(log => new { log.EmployeeId, log.PunchTime });
                entity.Property(log => log.PunchTime).HasColumnType("timestamp without time zone");
                entity.Property(log => log.ReceivedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(log => log.BiometricDevice)
                    .WithMany(device => device.AttendanceLogs)
                    .HasForeignKey(log => log.BiometricDeviceId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(log => log.Employee)
                    .WithMany()
                    .HasForeignKey(log => log.EmployeeId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<WorkTask>(entity =>
            {
                entity.ToTable("Tasks");
                entity.HasIndex(task => task.ManagerId);
                entity.HasIndex(task => task.AssigneeId);
                entity.HasIndex(task => new { task.Status, task.DueDate });
                entity.Property(task => task.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(task => task.DueDate).HasColumnType("date");
                entity.HasOne(task => task.Manager).WithMany()
                    .HasForeignKey(task => task.ManagerId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(task => task.Assignee).WithMany()
                    .HasForeignKey(task => task.AssigneeId).OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
