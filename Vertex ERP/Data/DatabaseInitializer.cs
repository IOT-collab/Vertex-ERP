using VertexERP.Models;
using VertexERP.Services;

namespace VertexERP.Data
{
    public static class DatabaseInitializer
    {
        public static void SeedDevelopmentUsers(ApplicationDbContext context, string password)
        {
            UpsertDevelopmentUser(context, "employee", password, "Employee", "Employee");
            UpsertDevelopmentUser(context, "admin", password, "Admin", "Admin");
            UpsertDevelopmentUser(context, "hr", password, "HR", "HR");
            context.SaveChanges();
        }

        private static void UpsertDevelopmentUser(
            ApplicationDbContext context,
            string username,
            string password,
            string role,
            string fullName)
        {
            var normalizedUsername = NormalizeUsername(username);
            var user = context.AppUsers.SingleOrDefault(
                existingUser => existingUser.NormalizedUsername == normalizedUsername);

            if (user == null)
            {
                context.AppUsers.Add(new AppUser
                {
                    Username = username,
                    NormalizedUsername = normalizedUsername,
                    PasswordHash = PasswordHashService.HashPassword(password),
                    Role = role,
                    FullName = fullName,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
                return;
            }

            user.Username = username;
            user.Role = role;
            user.FullName = fullName;
            user.IsActive = true;

            if (!PasswordHashService.VerifyPassword(password, user.PasswordHash))
            {
                user.PasswordHash = PasswordHashService.HashPassword(password);
            }
        }

        public static void SeedAdminUser(
            ApplicationDbContext context,
            string username,
            string password,
            string fullName)
        {
            var normalizedUsername = NormalizeUsername(username);
            if (context.AppUsers.Any(user => user.NormalizedUsername == normalizedUsername))
            {
                return;
            }

            context.AppUsers.Add(new AppUser
            {
                Username = username.Trim(),
                NormalizedUsername = normalizedUsername,
                PasswordHash = PasswordHashService.HashPassword(password),
                Role = "Admin",
                FullName = fullName.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

            context.SaveChanges();
        }

        public static void SeedDefaultUsers(ApplicationDbContext context)
        {
            context.Database.EnsureCreated();

            AddUserIfMissing(context, "admin", "Vertex1234", "Admin", "System Administrator");
            AddUserIfMissing(context, "user", "User1234", "User", "ERP User");
            AddUserIfMissing(context, "supervisor", "Supervisor1234", "Supervisor", "ERP Supervisor");

            var employees = new[]
            {
            ("VAS0156", "Ajay1234", "User", "Ajay Rajbhar"),
            ("VAS0122", "Amit1234", "User", "Amit Bhati"),
            ("VAS0171", "Bani1234", "User", "Bani Singh"),
            ("VAS0113", "Chandra1234", "User", "Chandra Bhan Singh"),
            ("VPC0145", "Dashrath1234", "User", "Dashrath Kumar Yadav"),
            ("VAS0114", "Gaurav1234", "User", "Gaurav Anand"),
            ("A012", "Govind1234", "User", "Govind"),
            ("VPC0160", "Kapil1234", "User", "Kapil"),
            ("VPC0124", "Kuldeep1234", "User", "Kuldeep Kumar Singh"),
            ("VAS0172", "Kush1234", "User", "Kush"),
            ("VPC", "Manoj1234", "User", "Manoj Pantry"),
            ("P010", "Adnan1234", "User", "Adnan"),
            ("VAS0155", "Mukesh1234", "User", "Mukesh Kumar"),
            ("VPC0125", "Naveen1234", "User", "Naveen Kumar"),
            ("VAS0173", "Pankaj1234", "User", "Pankaj Kumar"),
            ("VAS0161", "Pawan1234", "User", "Pawan"),
            ("P005", "Prempal1234", "User", "Prempal"),
            ("VAS0131", "Quaiser1234", "User", "Quaiser Reza"),
            ("VPC0149", "Rahul1234", "User", "Rahul Kumar"),
            ("VAS0174", "Rajdev1234", "User", "Rajdev Roy"),
            ("VAS0162", "Rajkumar1234", "User", "Rajkumar"),
            ("VAS0163", "Rajkumar1234", "User", "Rajkumar"),
            ("P008", "Rajnish1234", "User", "Rajnish Sharma"),
            ("P007", "Ramesh1234", "User", "Ramesh Kumar Yadav"),
            ("VPC0148", "Ranveer1234", "User", "Ranveer Kumar Yadav"),
            ("VAS0152", "Risbhabh1234", "User", "Risbhabh Vishwakarma"),
            ("VPC0165", "Sakshi1234", "User", "Sakshi Katariya"),
            ("VPC0142", "Sanoj1234", "User", "Sanoj Kumar"),
            ("VAS0153", "Sarvesh1234", "User", "Sarvesh Yadav"),
            ("VAS0146", "Satya1234", "User", "Satya Kumar"),
            ("VAS0167", "Shikhar1234", "User", "Shikhar Rathore"),
            ("VAS0119", "Shriram1234", "User", "Shriram Sharma"),
            ("VPC0166", "Sonu1234", "User", "Sonu"),
            ("A005", "Surajpal1234", "User", "Surajpal"),
            ("VPC0121", "Sushil1234", "User", "Sushil Kumar Tyagi"),
            ("VAS0132", "Tej1234", "User", "Tej Pratap"),
            ("VPC0161", "Updesh1234", "User", "Updesh Kumar"),
            ("VAS0159", "Vineet1234", "User", "Vineet Malik"),
            ("VPC0163", "Yuvraj1234", "User", "Yuvraj Singh"),
            ("A014", "Sourabh1234", "User", "Sourabh Saini"),
            ("79", "Ashish1234", "User", "Ashish Srivastava"),
            ("P020", "Dinesh1234", "User", "Dinesh Kumar"),
            ("VAS0179", "Chetan1234", "User", "Chetan Sharma"),
            ("P019", "Gaurav1234", "User", "Gaurav"),
            ("P015", "Amit1234", "User", "Amit"),
            ("VPC0167", "Gourav1234", "User", "Gourav Kumar"),
            ("P016", "Tinku1234", "User", "Tinku"),
            ("VPC0147", "Nihal1234", "User", "Nihal"),
            ("P021", "Shubham1234", "User", "Shubham"),
            ("P022", "Prabhakar1234", "User", "Prabhakar"),
            ("P023", "Nishu1234", "User", "Nishu"),
            ("A024", "Arjun1234", "User", "Arjun"),
            ("P025", "Dheerendra1234", "User", "Dheerendra"),
            ("P026", "Manoj1234", "User", "Manoj"),
            ("A020", "Brijpal1234", "User", "Brijpal"),
            ("A021", "Sunny1234", "User", "Sunny"),
            ("A022", "Jaya1234", "User", "Jaya"),
            ("A023", "Raju1234", "User", "Raju Vishwakarma"),
            ("P028", "Pradeep1234", "User", "Pradeep Kumar"),
            ("A025", "Vishal1234", "User", "Vishal"),
            ("A026", "Shreya1234", "User", "Shreya Raj"),
            };

            foreach (var employee in employees)
            {
                AddUserIfMissing(context, employee.Item1, employee.Item2, employee.Item3, employee.Item4);
            }

            context.SaveChanges();
        }

        private static void AddUserIfMissing(
            ApplicationDbContext context,
            string username,
            string password,
            string role,
            string fullName)
        {
            var normalizedUsername = NormalizeUsername(username);
            if (context.AppUsers.Any(user => user.NormalizedUsername == normalizedUsername))
            {
                return;
            }

            context.AppUsers.Add(new AppUser
            {
                Username = username,
                NormalizedUsername = normalizedUsername,
                PasswordHash = PasswordHashService.HashPassword(password),
                Role = role,
                FullName = fullName,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        public static string NormalizeUsername(string username)
        {
            return username.Trim().ToUpperInvariant();
        }
    }
}
