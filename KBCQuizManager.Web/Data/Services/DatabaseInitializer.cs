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
            // Apply pending migrations
            await _context.Database.MigrateAsync();
            _logger.LogInformation("Database migrations applied successfully");
            
            // Check if SuperAdmin exists
            var superAdminExists = await _context.Users
                .AnyAsync(u => u.Role == UserRole.SuperAdmin);
            
            if (!superAdminExists)
            {
                await CreateSuperAdminAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing database");
            throw;
        }
    }
    
    private async Task CreateSuperAdminAsync()
    {
        // Get SuperAdmin credentials from configuration or use defaults
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
            CreatedAt = DateTime.UtcNow
        };
        
        var result = await _userManager.CreateAsync(superAdmin, superAdminPassword);
        
        if (result.Succeeded)
        {
            _logger.LogInformation("SuperAdmin created successfully: {Email}", superAdminEmail);
            _logger.LogWarning("Default SuperAdmin credentials - Email: {Email}, Password: {Password}. Please change immediately!", 
                superAdminEmail, superAdminPassword);
        }
        else
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogError("Failed to create SuperAdmin: {Errors}", errors);
            throw new Exception($"Failed to create SuperAdmin: {errors}");
        }
    }
}
