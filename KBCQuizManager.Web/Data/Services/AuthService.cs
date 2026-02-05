using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using KBCQuizManager.Web.Data.Entities;

namespace KBCQuizManager.Web.Data.Services;

public interface IAuthService
{
    Task<(bool Success, string Message)> LoginAsync(string email, string password);
    Task LogoutAsync();
    Task<ApplicationUser?> GetCurrentUserAsync();
    Task<bool> IsAuthenticatedAsync();
    Task<(bool Success, string Message)> ChangePasswordAsync(string currentPassword, string newPassword);
    
    // Player registration
    Task<(bool Success, string Message, string? VerificationToken)> RegisterPlayerAsync(
        string firstName, string lastName, string email, string password, string adminCode);
    Task<(bool Success, string Message)> VerifyEmailAsync(string email, string token);
    Task<(bool Success, string Message)> ResendVerificationAsync(string email);
}

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ApplicationDbContext _context;
    private readonly IEmailService _emailService;
    
    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IHttpContextAccessor httpContextAccessor,
        ApplicationDbContext context,
        IEmailService emailService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _httpContextAccessor = httpContextAccessor;
        _context = context;
        _emailService = emailService;
    }
    
    private string GetBaseUrl()
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request == null)
        {
            // Fallback if HttpContext is not available (shouldn't happen in normal scenarios)
            return "https://localhost:5001";
        }
        
        var scheme = request.Scheme;
        var host = request.Host.ToUriComponent();
        return $"{scheme}://{host}";
    }
    
    public async Task<(bool Success, string Message)> LoginAsync(string email, string password)
    {
        var user = await _userManager.FindByEmailAsync(email);
        
        if (user == null)
            return (false, "Invalid email or password");
            
        if (!user.IsActive)
            return (false, "Your account has been deactivated. Please contact the administrator.");
        
        if (!user.EmailConfirmed)
            return (false, "Please verify your email before logging in. Check your inbox for the verification link.");
        
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

    // ===== Player Registration =====
    
    public async Task<(bool Success, string Message, string? VerificationToken)> RegisterPlayerAsync(
        string firstName, string lastName, string email, string password, string adminCode)
    {
        // Validate admin code
        var admin = await _context.Users
            .FirstOrDefaultAsync(u => u.AdminCode == adminCode.Trim().ToUpper() && u.IsActive 
                && (u.Role == UserRole.Admin || u.Role == UserRole.SuperAdmin));
        
        if (admin == null)
            return (false, "Invalid admin code. Please check with your administrator.", null);
        
        // Check if email already exists
        var existingUser = await _userManager.FindByEmailAsync(email);
        if (existingUser != null)
        {
            if (!existingUser.EmailConfirmed)
                return (false, "An account with this email already exists but is not verified. Please check your email or use 'Resend Verification'.", null);
            return (false, "An account with this email already exists. Please login instead.", null);
        }
        
        // Generate verification token
        var verificationToken = ApplicationUser.GenerateVerificationToken();
        
        var player = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Role = UserRole.Player,
            IsActive = true,
            EmailConfirmed = false,
            CreatedAt = DateTime.UtcNow,
            LinkedAdminId = admin.Id,
            EmailVerificationToken = verificationToken,
            EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(24)
        };
        
        var result = await _userManager.CreateAsync(player, password);
        
        if (result.Succeeded)
        {
            // Generate verification URL dynamically from current request
            var baseUrl = GetBaseUrl();
            var verificationUrl = $"{baseUrl}/account/verify-email?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(verificationToken)}";
            
            // Send verification email
            var emailSent = await _emailService.SendVerificationEmailAsync(email, firstName, verificationUrl);
            if (!emailSent)
            {
                // Log warning but don't fail registration
                // The token is still returned so user can verify manually if needed
            }
            
            return (true, "Registration successful! Please check your email to verify your account.", verificationToken);
        }
        
        var errors = string.Join(", ", result.Errors.Select(e => e.Description));
        return (false, errors, null);
    }
    
    public async Task<(bool Success, string Message)> VerifyEmailAsync(string email, string token)
    {
        var user = await _userManager.FindByEmailAsync(email);
        
        if (user == null)
            return (false, "User not found.");
        
        if (user.EmailConfirmed)
            return (true, "Email is already verified. You can login now.");
        
        if (user.EmailVerificationToken != token)
            return (false, "Invalid verification token.");
        
        if (user.EmailVerificationTokenExpiry.HasValue && user.EmailVerificationTokenExpiry < DateTime.UtcNow)
            return (false, "Verification token has expired. Please request a new one.");
        
        user.EmailConfirmed = true;
        user.EmailVerificationToken = null;
        user.EmailVerificationTokenExpiry = null;
        
        await _userManager.UpdateAsync(user);
        
        return (true, "Email verified successfully! You can now login.");
    }
    
    public async Task<(bool Success, string Message)> ResendVerificationAsync(string email)
    {
        var user = await _userManager.FindByEmailAsync(email);
        
        if (user == null)
            return (false, "No account found with this email.");
        
        if (user.EmailConfirmed)
            return (false, "Email is already verified. Please login.");
        
        var verificationToken = ApplicationUser.GenerateVerificationToken();
        user.EmailVerificationToken = verificationToken;
        user.EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(24);
        
        await _userManager.UpdateAsync(user);
        
        // Generate verification URL dynamically from current request
        var baseUrl = GetBaseUrl();
        var verificationUrl = $"{baseUrl}/account/verify-email?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(verificationToken)}";
        
        // Send verification email
        var emailSent = await _emailService.SendVerificationEmailAsync(email, user.FirstName, verificationUrl);
        if (emailSent)
        {
            return (true, "Verification email has been sent. Please check your inbox.");
        }
        else
        {
            return (true, $"Verification link has been regenerated. Please use this token: {verificationToken}");
        }
    }
}
