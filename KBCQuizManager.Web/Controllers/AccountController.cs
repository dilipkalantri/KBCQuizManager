using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using KBCQuizManager.Web.Data.Entities;
using System.Security.Claims;

namespace KBCQuizManager.Web.Controllers;

[Route("account")]
public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<AccountController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
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

            var result = await _signInManager.PasswordSignInAsync(user, password, isPersistent: true, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                _logger.LogInformation("Login successful for user {Email}", email);
                user.LastLoginAt = DateTime.UtcNow;
                await _userManager.UpdateAsync(user);
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

    [HttpGet("logout")]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return Redirect("/login");
    }
}
