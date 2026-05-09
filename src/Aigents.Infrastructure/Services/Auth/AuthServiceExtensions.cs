using Aigents.Domain.Entities;
using Aigents.Infrastructure.Data;
using Aigents.Infrastructure.Services.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aigents.Infrastructure.Services.Auth;

public static class AuthServiceExtensions
{
    /// <summary>
    /// Wires SMTP, the Identity email sender, and the login-alert service.
    /// Identity itself (cookie or JWT) is configured per-host in the calling
    /// project (Web vs. API have different defaults).
    /// </summary>
    public static IServiceCollection AddAigentsAuthCore(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<SmtpOptions>(config.GetSection(SmtpOptions.SectionName));
        services.AddSingleton<ITransactionalEmailSender, SmtpEmailSender>();
        services.AddScoped<IEmailSender<User>, IdentityEmailSender>();
        services.AddScoped<ILoginAlertService, LoginAlertService>();
        return services;
    }

    /// <summary>
    /// Registers the Identity user/role services against AigentsDbContext with
    /// the password / lockout / email-confirmation policy unrealestate.au uses.
    /// </summary>
    public static IdentityBuilder AddAigentsIdentityCore(this IServiceCollection services)
    {
        var builder = services.AddIdentityCore<User>(options =>
        {
            // Email confirmation is required before login.
            options.SignIn.RequireConfirmedEmail = true;

            // Password policy — 10+ chars, mix of categories, no specific punctuation.
            options.Password.RequiredLength = 10;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = false;

            // Lockout: 5 strikes, 15 minutes.
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.AllowedForNewUsers = true;

            // Treat email as the username — registrations are email-first.
            options.User.RequireUniqueEmail = true;

            // Tokens (confirmation, reset) are valid for 1 hour.
            options.Tokens.PasswordResetTokenProvider = TokenOptions.DefaultProvider;
            options.Tokens.EmailConfirmationTokenProvider = TokenOptions.DefaultProvider;
        });

        builder
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AigentsDbContext>()
            .AddDefaultTokenProviders();

        return builder;
    }
}
