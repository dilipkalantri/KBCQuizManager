using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using KBCQuizManager.Web.Data.Entities;

namespace KBCQuizManager.Web.Data.Services;

public interface IUserService
{
    Task<List<ApplicationUser>> GetAllAdminsAsync();
    Task<ApplicationUser?> GetUserByIdAsync(Guid id);
    Task<(bool Success, string Message)> CreateAdminAsync(ApplicationUser user, string password, Guid createdById);
    Task<(bool Success, string Message)> UpdateUserAsync(ApplicationUser user);
    Task<(bool Success, string Message)> DeleteUserAsync(Guid id);
    Task<(bool Success, string Message)> ToggleUserStatusAsync(Guid id);
    Task<(bool Success, string Message)> ResetPasswordAsync(Guid userId, string newPassword);
    Task<UserStatistics> GetUserStatisticsAsync(Guid userId);
    
    // Admin code
    Task<ApplicationUser?> ValidateAdminCodeAsync(string code);
    Task<string?> GetAdminCodeAsync(Guid adminId);
    Task<(bool Success, string Message)> RegenerateAdminCodeAsync(Guid adminId);
    
    // Public users
    Task<PublicUser> RegisterOrGetPublicUserAsync(string name, string? email, Guid adminId);
    Task<(List<PublicUser> Users, int TotalCount)> GetPublicUsersByAdminAsync(Guid adminId, int page, int pageSize, string? search = null);
    Task<(bool Success, string Message)> DeletePublicUserAsync(Guid publicUserId, Guid adminId);
}

public class UserService : IUserService
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    
    public UserService(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }
    
    public async Task<List<ApplicationUser>> GetAllAdminsAsync()
    {
        return await _context.Users
            .Where(u => u.Role == UserRole.Admin)
            .Include(u => u.CreatedBy)
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();
    }
    
    public async Task<ApplicationUser?> GetUserByIdAsync(Guid id)
    {
        return await _context.Users
            .Include(u => u.Categories)
            .Include(u => u.Questions)
            .FirstOrDefaultAsync(u => u.Id == id);
    }
    
    public async Task<(bool Success, string Message)> CreateAdminAsync(ApplicationUser user, string password, Guid createdById)
    {
        // Check if email already exists
        var existingUser = await _userManager.FindByEmailAsync(user.Email!);
        if (existingUser != null)
            return (false, "A user with this email already exists");
        
        // Generate unique admin code
        var existingCodes = await _context.Users
            .Where(u => u.AdminCode != null)
            .Select(u => u.AdminCode!)
            .ToListAsync();
        
        string code;
        do
        {
            code = ApplicationUser.GenerateAdminCode();
        } while (existingCodes.Contains(code));
        
        user.UserName = user.Email;
        user.Role = UserRole.Admin;
        user.CreatedById = createdById;
        user.CreatedAt = DateTime.UtcNow;
        user.EmailConfirmed = true;
        user.AdminCode = code;
        
        var result = await _userManager.CreateAsync(user, password);
        
        if (result.Succeeded)
            return (true, $"Admin created successfully. Admin Code: {code}");
            
        var errors = string.Join(", ", result.Errors.Select(e => e.Description));
        return (false, errors);
    }
    
    public async Task<(bool Success, string Message)> UpdateUserAsync(ApplicationUser user)
    {
        var existingUser = await _context.Users.FindAsync(user.Id);
        if (existingUser == null)
            return (false, "User not found");
        
        existingUser.FirstName = user.FirstName;
        existingUser.LastName = user.LastName;
        existingUser.Email = user.Email;
        existingUser.UserName = user.Email;
        existingUser.PhoneNumber = user.PhoneNumber;
        
        await _context.SaveChangesAsync();
        return (true, "User updated successfully");
    }
    
    public async Task<(bool Success, string Message)> DeleteUserAsync(Guid id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return (false, "User not found");
            
        if (user.Role == UserRole.SuperAdmin)
            return (false, "Cannot delete Super Admin");
        
        _context.Users.Remove(user);
        await _context.SaveChangesAsync();
        return (true, "User deleted successfully");
    }
    
    public async Task<(bool Success, string Message)> ToggleUserStatusAsync(Guid id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return (false, "User not found");
            
        if (user.Role == UserRole.SuperAdmin)
            return (false, "Cannot deactivate Super Admin");
        
        user.IsActive = !user.IsActive;
        await _context.SaveChangesAsync();
        
        var status = user.IsActive ? "activated" : "deactivated";
        return (true, $"User {status} successfully");
    }
    
    public async Task<(bool Success, string Message)> ResetPasswordAsync(Guid userId, string newPassword)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            return (false, "User not found");
        
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        
        if (result.Succeeded)
            return (true, "Password reset successfully");
            
        var errors = string.Join(", ", result.Errors.Select(e => e.Description));
        return (false, errors);
    }
    
    public async Task<UserStatistics> GetUserStatisticsAsync(Guid userId)
    {
        var categoriesCount = await _context.Categories.CountAsync(c => c.OwnerId == userId);
        var questionsCount = await _context.Questions.CountAsync(q => q.OwnerId == userId);
        var activeQuestionsCount = await _context.Questions.CountAsync(q => q.OwnerId == userId && q.IsActive);
        var publicUsersCount = await _context.PublicUsers.CountAsync(p => p.AdminId == userId);
        
        return new UserStatistics
        {
            TotalCategories = categoriesCount,
            TotalQuestions = questionsCount,
            ActiveQuestions = activeQuestionsCount,
            RegisteredUsers = publicUsersCount
        };
    }
    
    // ===== Admin Code Methods =====
    
    public async Task<ApplicationUser?> ValidateAdminCodeAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        
        return await _context.Users
            .FirstOrDefaultAsync(u => u.AdminCode == code.Trim().ToUpper() && u.IsActive);
    }
    
    public async Task<string?> GetAdminCodeAsync(Guid adminId)
    {
        return await _context.Users
            .Where(u => u.Id == adminId)
            .Select(u => u.AdminCode)
            .FirstOrDefaultAsync();
    }
    
    public async Task<(bool Success, string Message)> RegenerateAdminCodeAsync(Guid adminId)
    {
        var user = await _context.Users.FindAsync(adminId);
        if (user == null)
            return (false, "User not found");
        
        var existingCodes = await _context.Users
            .Where(u => u.AdminCode != null && u.Id != adminId)
            .Select(u => u.AdminCode!)
            .ToListAsync();
        
        string code;
        do
        {
            code = ApplicationUser.GenerateAdminCode();
        } while (existingCodes.Contains(code));
        
        user.AdminCode = code;
        await _context.SaveChangesAsync();
        
        return (true, $"New code generated: {code}");
    }
    
    // ===== Public User Methods =====
    
    public async Task<PublicUser> RegisterOrGetPublicUserAsync(string name, string? email, Guid adminId)
    {
        // Check if user already exists with same name for this admin
        var existing = await _context.PublicUsers
            .FirstOrDefaultAsync(p => p.AdminId == adminId && p.Name.ToLower() == name.Trim().ToLower());
        
        if (existing != null)
            return existing;
        
        var publicUser = new PublicUser
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Email = email?.Trim(),
            AdminId = adminId,
            RegisteredAt = DateTime.UtcNow
        };
        
        _context.PublicUsers.Add(publicUser);
        await _context.SaveChangesAsync();
        
        return publicUser;
    }
    
    public async Task<(List<PublicUser> Users, int TotalCount)> GetPublicUsersByAdminAsync(Guid adminId, int page, int pageSize, string? search = null)
    {
        var query = _context.PublicUsers
            .Where(p => p.AdminId == adminId);
        
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(s) || (p.Email != null && p.Email.ToLower().Contains(s)));
        }
        
        var totalCount = await query.CountAsync();
        
        var users = await query
            .OrderByDescending(p => p.RegisteredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        
        return (users, totalCount);
    }
    
    public async Task<(bool Success, string Message)> DeletePublicUserAsync(Guid publicUserId, Guid adminId)
    {
        var user = await _context.PublicUsers
            .FirstOrDefaultAsync(p => p.Id == publicUserId && p.AdminId == adminId);
        
        if (user == null)
            return (false, "User not found");
        
        _context.PublicUsers.Remove(user);
        await _context.SaveChangesAsync();
        return (true, "User removed successfully");
    }
}

public class UserStatistics
{
    public int TotalCategories { get; set; }
    public int TotalQuestions { get; set; }
    public int ActiveQuestions { get; set; }
    public int RegisteredUsers { get; set; }
}
