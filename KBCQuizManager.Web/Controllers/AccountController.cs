using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using KBCQuizManager.Web.Data.Entities;
using KBCQuizManager.Web.Data.Services;
using System.Security.Claims;

namespace KBCQuizManager.Web.Controllers;

[Route("account")]
public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<AccountController> _logger;
    private readonly IAuthService _authService;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<AccountController> logger,
        IAuthService authService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
        _authService = authService;
    }

    [HttpPost("login")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Login([FromForm] string email, [FromForm] string password, [FromForm] string? returnUrl = null)
    {
        returnUrl ??= "/";

        try
        {
            _logger.LogInformation("Login attempt for email: {Email}", email);

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                return Redirect($"/login?error=Please enter email and password");
            }

            var user = await _userManager.FindByEmailAsync(email);
            
            if (user == null)
            {
                _logger.LogWarning("Login failed: User not found for email {Email}", email);
                return Redirect($"/login?error=Invalid email or password");
            }
            
            if (!user.IsActive)
            {
                _logger.LogWarning("Login failed: User {Email} is deactivated", email);
                return Redirect($"/login?error=Your account has been deactivated");
            }
            
            if (!user.EmailConfirmed)
            {
                _logger.LogWarning("Login failed: Email not verified for {Email}", email);
                return Redirect($"/login?error=Please verify your email before logging in&email={Uri.EscapeDataString(email)}");
            }

            var result = await _signInManager.PasswordSignInAsync(user, password, isPersistent: true, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                _logger.LogInformation("Login successful for user {Email} with role {Role}", email, user.Role);
                user.LastLoginAt = DateTime.UtcNow;
                await _userManager.UpdateAsync(user);
                
                // Redirect based on role
                if (user.Role == UserRole.Player)
                {
                    return LocalRedirect("/player");
                }
                return LocalRedirect(returnUrl);
            }

            if (result.IsLockedOut)
            {
                _logger.LogWarning("Login failed: User {Email} is locked out", email);
                return Redirect($"/login?error=Account is locked. Please try again later.");
            }

            _logger.LogWarning("Login failed: Invalid password for user {Email}", email);
            return Redirect($"/login?error=Invalid email or password");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login error for email {Email}", email);
            return Redirect($"/login?error=An error occurred. Please try again.");
        }
    }

    [HttpPost("register")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Register(
        [FromForm] string firstName,
        [FromForm] string lastName,
        [FromForm] string email,
        [FromForm] string password,
        [FromForm] string adminCode)
    {
        try
        {
            _logger.LogInformation("Registration attempt for email: {Email}", email);

            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName) 
                || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) 
                || string.IsNullOrWhiteSpace(adminCode))
            {
                return Redirect($"/register?error=All fields are required");
            }

            var (success, message, token) = await _authService.RegisterPlayerAsync(
                firstName, lastName, email, password, adminCode);

            if (success)
            {
                // Email verification link has been sent via email
                // Redirect to a page informing the user to check their email
                return Redirect($"/verify-email?email={Uri.EscapeDataString(email)}&sent=true");
            }

            return Redirect($"/register?error={Uri.EscapeDataString(message)}&firstName={Uri.EscapeDataString(firstName)}&lastName={Uri.EscapeDataString(lastName)}&email={Uri.EscapeDataString(email)}&adminCode={Uri.EscapeDataString(adminCode)}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration error for email {Email}", email);
            return Redirect($"/register?error=An error occurred. Please try again.");
        }
    }

    [HttpGet("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromQuery] string email, [FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
        {
            return Redirect("/verify-email?error=Invalid verification link");
        }

        var (success, message) = await _authService.VerifyEmailAsync(email, token);
        
        if (success)
        {
            return Redirect($"/verify-email?success=true&email={Uri.EscapeDataString(email)}");
        }

        return Redirect($"/verify-email?error={Uri.EscapeDataString(message)}&email={Uri.EscapeDataString(email)}");
    }

    [HttpGet("logout")]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return Redirect("/login");
    }
}
