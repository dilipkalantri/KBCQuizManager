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
        
        user.UserName = user.Email;
        user.Role = UserRole.Admin;
        user.CreatedById = createdById;
        user.CreatedAt = DateTime.UtcNow;
        user.EmailConfirmed = true;
        
        var result = await _userManager.CreateAsync(user, password);
        
        if (result.Succeeded)
            return (true, "Admin created successfully");
            
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
        
        return new UserStatistics
        {
            TotalCategories = categoriesCount,
            TotalQuestions = questionsCount,
            ActiveQuestions = activeQuestionsCount
        };
    }
}

public class UserStatistics
{
    public int TotalCategories { get; set; }
    public int TotalQuestions { get; set; }
    public int ActiveQuestions { get; set; }
}
