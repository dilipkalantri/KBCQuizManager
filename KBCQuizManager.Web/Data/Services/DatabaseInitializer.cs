using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using KBCQuizManager.Web.Data.Entities;

namespace KBCQuizManager.Web.Data.Services;

public interface IDatabaseInitializer
{
    Task InitializeAsync();
}

public class DatabaseInitializer : IDatabaseInitializer
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseInitializer> _logger;
    
    public DatabaseInitializer(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        ILogger<DatabaseInitializer> logger)
    {
        _context = context;
        _userManager = userManager;
        _configuration = configuration;
        _logger = logger;
    }
    
    public async Task InitializeAsync()
    {
        try
        {
            // Create database and tables if they don't exist
            await _context.Database.EnsureCreatedAsync();
            _logger.LogInformation("Database created/verified successfully");
            
            // Auto-migrate: Add new columns/tables if missing
            await RunMigrationsAsync();
            
            // Check if SuperAdmin exists
            var superAdminExists = await _context.Users
                .AnyAsync(u => u.Role == UserRole.SuperAdmin);
            
            if (!superAdminExists)
            {
                await CreateSuperAdminAsync();
            }
            
            // Generate admin codes for any admins that don't have one
            await GenerateAdminCodesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing database");
            throw;
        }
    }
    
    private async Task CreateSuperAdminAsync()
    {
        var superAdminEmail = _configuration["SuperAdmin:Email"] ?? "admin@kbcquiz.com";
        var superAdminPassword = _configuration["SuperAdmin:Password"] ?? "Admin@123456";
        var superAdminFirstName = _configuration["SuperAdmin:FirstName"] ?? "Super";
        var superAdminLastName = _configuration["SuperAdmin:LastName"] ?? "Admin";
        
        var superAdmin = new ApplicationUser
        {
            UserName = superAdminEmail,
            Email = superAdminEmail,
            FirstName = superAdminFirstName,
            LastName = superAdminLastName,
            Role = UserRole.SuperAdmin,
            IsActive = true,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow,
            AdminCode = ApplicationUser.GenerateAdminCode()
        };
        
        var result = await _userManager.CreateAsync(superAdmin, superAdminPassword);
        
        if (result.Succeeded)
        {
            _logger.LogInformation("SuperAdmin created successfully: {Email} with code: {Code}", superAdminEmail, superAdmin.AdminCode);
        }
        else
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogError("Failed to create SuperAdmin: {Errors}", errors);
            throw new Exception($"Failed to create SuperAdmin: {errors}");
        }
    }
    
    private async Task GenerateAdminCodesAsync()
    {
        try
        {
            var adminsWithoutCode = await _context.Users
                .Where(u => u.AdminCode == null || u.AdminCode == "")
                .ToListAsync();
            
            if (adminsWithoutCode.Any())
            {
                var existingCodes = await _context.Users
                    .Where(u => u.AdminCode != null)
                    .Select(u => u.AdminCode!)
                    .ToListAsync();
                
                foreach (var admin in adminsWithoutCode)
                {
                    string code;
                    do
                    {
                        code = ApplicationUser.GenerateAdminCode();
                    } while (existingCodes.Contains(code));
                    
                    admin.AdminCode = code;
                    existingCodes.Add(code);
                    _logger.LogInformation("Generated admin code {Code} for user {Email}", code, admin.Email);
                }
                
                await _context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error generating admin codes (columns may not exist yet)");
        }
    }
    
    private async Task RunMigrationsAsync()
    {
        try
        {
            var conn = _context.Database.GetDbConnection();
            await conn.OpenAsync();
            
            // Migration: Add IsHost column to MultiplayerPlayers
            await AddColumnIfMissing(conn, "MultiplayerPlayers", "IsHost", @"ALTER TABLE ""MultiplayerPlayers"" ADD COLUMN ""IsHost"" boolean NOT NULL DEFAULT false");
            
            // Migration: Add AdminCode column to Users
            await AddColumnIfMissing(conn, "Users", "AdminCode", @"ALTER TABLE ""Users"" ADD COLUMN ""AdminCode"" text");
            
            // Migration: Create PublicUsers table
            await CreateTableIfMissing(conn, "PublicUsers", @"
                CREATE TABLE ""PublicUsers"" (
                    ""Id"" uuid NOT NULL,
                    ""Name"" text NOT NULL,
                    ""Email"" text,
                    ""AdminId"" uuid NOT NULL,
                    ""RegisteredAt"" timestamp with time zone NOT NULL DEFAULT NOW(),
                    ""GamesPlayed"" integer NOT NULL DEFAULT 0,
                    ""TotalScore"" integer NOT NULL DEFAULT 0,
                    CONSTRAINT ""PK_PublicUsers"" PRIMARY KEY (""Id""),
                    CONSTRAINT ""FK_PublicUsers_Users_AdminId"" FOREIGN KEY (""AdminId"") REFERENCES ""Users""(""Id"") ON DELETE CASCADE
                )");
            
            // Migration: Add LinkedAdminId column to Users (for Player role)
            await AddColumnIfMissing(conn, "Users", "LinkedAdminId", @"ALTER TABLE ""Users"" ADD COLUMN ""LinkedAdminId"" uuid REFERENCES ""Users""(""Id"") ON DELETE SET NULL");
            
            // Migration: Add EmailVerificationToken column to Users
            await AddColumnIfMissing(conn, "Users", "EmailVerificationToken", @"ALTER TABLE ""Users"" ADD COLUMN ""EmailVerificationToken"" text");
            
            // Migration: Add EmailVerificationTokenExpiry column to Users
            await AddColumnIfMissing(conn, "Users", "EmailVerificationTokenExpiry", @"ALTER TABLE ""Users"" ADD COLUMN ""EmailVerificationTokenExpiry"" timestamp with time zone");
            
            // Migration: Add PlayerId column to GameSessions
            await AddColumnIfMissing(conn, "GameSessions", "PlayerId", @"ALTER TABLE ""GameSessions"" ADD COLUMN ""PlayerId"" uuid REFERENCES ""Users""(""Id"") ON DELETE SET NULL");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Migration check failed (some tables may not exist yet)");
        }
    }
    
    private async Task AddColumnIfMissing(System.Data.Common.DbConnection conn, string table, string column, string alterSql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT column_name FROM information_schema.columns WHERE table_name = '{table}' AND column_name = '{column}'";
        var result = await cmd.ExecuteScalarAsync();
        if (result == null)
        {
            _logger.LogInformation("Adding {Column} column to {Table} table...", column, table);
            using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = alterSql;
            await alterCmd.ExecuteNonQueryAsync();
            _logger.LogInformation("{Column} column added successfully", column);
        }
    }
    
    private async Task CreateTableIfMissing(System.Data.Common.DbConnection conn, string table, string createSql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT table_name FROM information_schema.tables WHERE table_name = '{table}'";
        var result = await cmd.ExecuteScalarAsync();
        if (result == null)
        {
            _logger.LogInformation("Creating {Table} table...", table);
            using var createCmd = conn.CreateCommand();
            createCmd.CommandText = createSql;
            await createCmd.ExecuteNonQueryAsync();
            _logger.LogInformation("{Table} table created successfully", table);
        }
    }
}
