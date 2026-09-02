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
        public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
        public DbSet<QueryTicket> QueryTickets => Set<QueryTicket>();
        public DbSet<EmployeeBankDetail> EmployeeBankDetails => Set<EmployeeBankDetail>();
        public DbSet<BankDetailUpdateRequest> BankDetailUpdateRequests => Set<BankDetailUpdateRequest>();
        public DbSet<EmployeeSalaryDetail> EmployeeSalaryDetails => Set<EmployeeSalaryDetail>();
        public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();
        public DbSet<EmployeeAsset> EmployeeAssets => Set<EmployeeAsset>();
        public DbSet<ExpenseClaim> ExpenseClaims => Set<ExpenseClaim>();
        public DbSet<RecruitmentHiringRecord> RecruitmentHiringRecords => Set<RecruitmentHiringRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.HasSequence<long>("EmployeeCodeSequence");
            modelBuilder.Entity<EmployeeAsset>(entity =>
            {
                entity.HasIndex(asset => asset.AssetTag).IsUnique();
                entity.HasOne(asset => asset.Employee).WithMany()
                    .HasForeignKey(asset => asset.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<AppUser>(entity =>
            {
                entity.ToTable("Users");
                entity.HasIndex(user => user.NormalizedUsername).IsUnique();
                entity.HasIndex(user => user.EmployeeId).IsUnique();
                entity.Property(user => user.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(user => user.Employee)
                    .WithOne()
                    .HasForeignKey<AppUser>(user => user.EmployeeId)
                    .OnDelete(DeleteBehavior.SetNull);
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
            modelBuilder.Entity<ExpenseClaim>(entity =>
            {
                entity.ToTable("ExpenseClaims");
                entity.HasIndex(claim => new { claim.EmployeeId, claim.SubmittedAtUtc });
                entity.HasIndex(claim => new { claim.ReportingManagerId, claim.Status });
                entity.Property(claim => claim.Amount).HasPrecision(18, 2);
                entity.Property(claim => claim.RequiresHrApproval).HasDefaultValue(false);
                entity.Property(claim => claim.SubmittedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(claim => claim.Employee).WithMany().HasForeignKey(claim => claim.EmployeeId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(claim => claim.ReportingManager).WithMany().HasForeignKey(claim => claim.ReportingManagerId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(claim => claim.DecidedByUser).WithMany().HasForeignKey(claim => claim.DecidedByUserId).OnDelete(DeleteBehavior.SetNull);
            });
            modelBuilder.Entity<RecruitmentHiringRecord>(entity =>
            {
                entity.ToTable("RecruitmentHiringRecords");
                entity.HasIndex(record => new { record.DepartmentId, record.Year, record.Month, record.WeekNumber }).IsUnique();
                entity.Property(record => record.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(record => record.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(record => record.Department).WithMany().HasForeignKey(record => record.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<LeaveRequest>(entity =>
            {
                entity.ToTable("LeaveRequests");
                entity.Property(request => request.AppliedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(request => request.Employee).WithMany()
                    .HasForeignKey(request => request.EmployeeId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(request => request.AssignedApproverEmployee).WithMany()
                    .HasForeignKey(request => request.AssignedApproverEmployeeId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(request => request.DecidedByUser).WithMany()
                    .HasForeignKey(request => request.DecidedByUserId).OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<QueryTicket>(entity =>
            {
                entity.ToTable("QueryTickets");
                entity.HasIndex(ticket => new { ticket.EmployeeId, ticket.CreatedAtUtc });
                entity.HasIndex(ticket => new { ticket.ReportingManagerId, ticket.Status });
                entity.Property(ticket => ticket.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(ticket => ticket.Employee).WithMany().HasForeignKey(ticket => ticket.EmployeeId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(ticket => ticket.ReportingManager).WithMany().HasForeignKey(ticket => ticket.ReportingManagerId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(ticket => ticket.ResolvedByUser).WithMany().HasForeignKey(ticket => ticket.ResolvedByUserId).OnDelete(DeleteBehavior.SetNull);
            });
            modelBuilder.Entity<EmployeeBankDetail>(entity => { entity.ToTable("EmployeeBankDetails"); entity.HasIndex(x => x.EmployeeId).IsUnique(); entity.HasOne(x => x.Employee).WithOne().HasForeignKey<EmployeeBankDetail>(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade); });
            modelBuilder.Entity<BankDetailUpdateRequest>(entity => { entity.ToTable("BankDetailUpdateRequests"); entity.HasIndex(x => new { x.EmployeeId, x.Status }); entity.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade); });
            modelBuilder.Entity<EmployeeSalaryDetail>(entity => { entity.ToTable("EmployeeSalaryDetails"); entity.HasIndex(x => x.EmployeeId).IsUnique(); entity.Property(x => x.BasicSalary).HasPrecision(18,2); entity.Property(x => x.HouseRentAllowance).HasPrecision(18,2); entity.Property(x => x.ConveyanceAllowance).HasPrecision(18,2); entity.Property(x => x.SpecialAllowance).HasPrecision(18,2); entity.Property(x => x.ProvidentFund).HasPrecision(18,2); entity.Property(x => x.ProfessionalTax).HasPrecision(18,2); entity.Property(x => x.Tds).HasPrecision(18,2); entity.Property(x => x.OtherDeductions).HasPrecision(18,2); entity.HasOne(x => x.Employee).WithOne().HasForeignKey<EmployeeSalaryDetail>(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade); });
            modelBuilder.Entity<EmployeeDocument>(entity =>
            {
                entity.ToTable("EmployeeDocuments");
                entity.HasIndex(x => new { x.EmployeeId, x.DocumentType });
                entity.Property(x => x.UploadedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
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
                entity.Property(log => log.Latitude).HasPrecision(9, 6);
                entity.Property(log => log.Longitude).HasPrecision(9, 6);
                entity.Property(log => log.AccuracyMetres).HasPrecision(10, 2);
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
