using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KBCQuizManager.Web.Data.Services;

public interface IEmailService
{
    Task<bool> SendEmailAsync(string toEmail, string subject, string body, bool isHtml = true);
    Task<bool> SendVerificationEmailAsync(string toEmail, string firstName, string verificationUrl);
    Task<bool> SendPasswordResetEmailAsync(string toEmail, string firstName, string resetUrl);
}

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;
    private readonly SmtpSettings _smtpSettings;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _smtpSettings = new SmtpSettings();
        _configuration.GetSection("SmtpSettings").Bind(_smtpSettings);
    }

    public async Task<bool> SendEmailAsync(string toEmail, string subject, string body, bool isHtml = true)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_smtpSettings.Host) || 
                string.IsNullOrWhiteSpace(_smtpSettings.Username) ||
                string.IsNullOrWhiteSpace(_smtpSettings.Password))
            {
                _logger.LogWarning("SMTP settings are not configured. Email will not be sent.");
                return false;
            }

            using var client = new SmtpClient(_smtpSettings.Host, _smtpSettings.Port)
            {
                EnableSsl = _smtpSettings.EnableSsl,
                UseDefaultCredentials = _smtpSettings.UseDefaultCredentials,
                Credentials = new NetworkCredential(_smtpSettings.Username, _smtpSettings.Password)
            };

            using var message = new MailMessage
            {
                From = new MailAddress(_smtpSettings.FromEmail, _smtpSettings.FromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = isHtml
            };

            message.To.Add(toEmail);

            await client.SendMailAsync(message);
            _logger.LogInformation("Email sent successfully to {Email}", toEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
            return false;
        }
    }

    public async Task<bool> SendVerificationEmailAsync(string toEmail, string firstName, string verificationUrl)
    {
        var subject = "Verify Your Email - KBC Quiz Manager";
        var body = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #4CAF50; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .button {{ display: inline-block; padding: 12px 24px; background-color: #4CAF50; color: white; text-decoration: none; border-radius: 5px; margin: 20px 0; }}
        .footer {{ text-align: center; padding: 20px; color: #666; font-size: 12px; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>KBC Quiz Manager</h1>
        </div>
        <div class=""content"">
            <h2>Hello {firstName}!</h2>
            <p>Thank you for registering with KBC Quiz Manager. Please verify your email address by clicking the button below:</p>
            <p style=""text-align: center;"">
                <a href=""{verificationUrl}"" class=""button"">Verify Email Address</a>
            </p>
            <p>Or copy and paste this link into your browser:</p>
            <p style=""word-break: break-all; color: #4CAF50;"">{verificationUrl}</p>
            <p>This verification link will expire in 24 hours.</p>
            <p>If you did not create an account, please ignore this email.</p>
        </div>
        <div class=""footer"">
            <p>&copy; {DateTime.Now.Year} KBC Quiz Manager. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";

        return await SendEmailAsync(toEmail, subject, body, true);
    }

    public async Task<bool> SendPasswordResetEmailAsync(string toEmail, string firstName, string resetUrl)
    {
        var subject = "Reset Your Password - KBC Quiz Manager";
        var body = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #2196F3; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .button {{ display: inline-block; padding: 12px 24px; background-color: #2196F3; color: white; text-decoration: none; border-radius: 5px; margin: 20px 0; }}
        .footer {{ text-align: center; padding: 20px; color: #666; font-size: 12px; }}
        .warning {{ background-color: #fff3cd; border-left: 4px solid #ffc107; padding: 10px; margin: 15px 0; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>KBC Quiz Manager</h1>
        </div>
        <div class=""content"">
            <h2>Hello {firstName}!</h2>
            <p>We received a request to reset your password. Click the button below to reset it:</p>
            <p style=""text-align: center;"">
                <a href=""{resetUrl}"" class=""button"">Reset Password</a>
            </p>
            <p>Or copy and paste this link into your browser:</p>
            <p style=""word-break: break-all; color: #2196F3;"">{resetUrl}</p>
            <div class=""warning"">
                <strong>Security Notice:</strong> This link will expire in 1 hour. If you did not request a password reset, please ignore this email and your password will remain unchanged.
            </div>
        </div>
        <div class=""footer"">
            <p>&copy; {DateTime.Now.Year} KBC Quiz Manager. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";

        return await SendEmailAsync(toEmail, subject, body, true);
    }
}

public class SmtpSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public bool UseDefaultCredentials { get; set; } = false;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
}

