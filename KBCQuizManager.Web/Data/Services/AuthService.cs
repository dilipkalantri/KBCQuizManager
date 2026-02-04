using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using KBCQuizManager.Web.Data.Entities;

namespace KBCQuizManager.Web.Data.Services;

public interface IAuthService
{
    Task<(bool Success, string Message)> LoginAsync(string email, string password);
    Task LogoutAsync();
    Task<ApplicationUser?> GetCurrentUserAsync();
    Task<bool> IsAuthenticatedAsync();
    Task<(bool Success, string Message)> ChangePasswordAsync(string currentPassword, string newPassword);
}

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IHttpContextAccessor _httpContextAccessor;
    
    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _httpContextAccessor = httpContextAccessor;
    }
    
    public async Task<(bool Success, string Message)> LoginAsync(string email, string password)
    {
        var user = await _userManager.FindByEmailAsync(email);
        
        if (user == null)
            return (false, "Invalid email or password");
            
        if (!user.IsActive)
            return (false, "Your account has been deactivated. Please contact the administrator.");
        
        var result = await _signInManager.PasswordSignInAsync(user, password, isPersistent: true, lockoutOnFailure: false);
        
        if (result.Succeeded)
        {
            user.LastLoginAt = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);
            return (true, "Login successful");
        }
        
        if (result.IsLockedOut)
            return (false, "Account is locked. Please try again later.");
            
        return (false, "Invalid email or password");
    }
    
    public async Task LogoutAsync()
    {
        await _signInManager.SignOutAsync();
    }
    
    public async Task<ApplicationUser?> GetCurrentUserAsync()
    {
        var userId = _httpContextAccessor.HttpContext?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        
        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var guidId))
            return null;
            
        return await _userManager.Users.FirstOrDefaultAsync(u => u.Id == guidId);
    }
    
    public Task<bool> IsAuthenticatedAsync()
    {
        var isAuthenticated = _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
        return Task.FromResult(isAuthenticated);
    }
    
    public async Task<(bool Success, string Message)> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
            return (false, "User not found");
            
        var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        
        if (result.Succeeded)
            return (true, "Password changed successfully");
            
        var errors = string.Join(", ", result.Errors.Select(e => e.Description));
        return (false, errors);
    }
}
