using Aigents.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Aigents.Infrastructure.Data;

/// <summary>
/// Entity Framework Core database context for Aigents. Extends IdentityDbContext
/// so ASP.NET Core Identity tables (AspNetUsers etc.) are owned by this context
/// — auth is sovereign-from-day-1, no third-party IdP.
/// </summary>
public class AigentsDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>
{
    public AigentsDbContext(DbContextOptions<AigentsDbContext> options) : base(options)
    {
    }

    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<ListingInquiry> ListingInquiries => Set<ListingInquiry>();
    public DbSet<ListingDistribution> ListingDistributions => Set<ListingDistribution>();
    public DbSet<Fido2Credential> Fido2Credentials => Set<Fido2Credential>();
    public DbSet<ProductEvent> ProductEvents => Set<ProductEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User configuration — Email/PhoneNumber/UserName already mapped by IdentityUser<Guid>.
        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.InterestedSuburb).HasMaxLength(100);
            entity.Property(e => e.LastLoginIp).HasMaxLength(64);
            entity.Property(e => e.LastLoginUserAgent).HasMaxLength(512);

            entity.HasMany(e => e.Conversations)
                .WithOne(c => c.User)
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Fido2Credentials)
                .WithOne(c => c.User)
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Conversation configuration
        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.CreatedAt);

            entity.Property(e => e.Summary).HasMaxLength(1000);

            entity.HasMany(e => e.Messages)
                .WithOne(m => m.Conversation)
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Message configuration
        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ConversationId);
            entity.HasIndex(e => e.CreatedAt);

            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.ModelUsed).HasMaxLength(50);
        });

        // Listing configuration
        modelBuilder.Entity<Listing>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Suburb);
            entity.HasIndex(e => e.Postcode);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);

            entity.Property(e => e.Address).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Suburb).HasMaxLength(100).IsRequired();
            entity.Property(e => e.State).HasMaxLength(10);
            entity.Property(e => e.Postcode).HasMaxLength(10);
            entity.Property(e => e.PropertyType).HasMaxLength(50);
            entity.Property(e => e.Headline).HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(5000);
            entity.Property(e => e.PriceDisplay).HasMaxLength(100);
            entity.Property(e => e.EstimatedValue).HasPrecision(18, 2);
            entity.Property(e => e.AskingPrice).HasPrecision(18, 2);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Inquiries)
                .WithOne(i => i.Listing)
                .HasForeignKey(i => i.ListingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Agent configuration
        modelBuilder.Entity<Agent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.IsActive);

            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.AgencyName).HasMaxLength(255);
            entity.Property(e => e.LicenseNumber).HasMaxLength(50);
            entity.Property(e => e.MinPropertyValue).HasPrecision(18, 2);
            entity.Property(e => e.MaxPropertyValue).HasPrecision(18, 2);
            entity.Property(e => e.TotalCommissionEarned).HasPrecision(18, 2);

            entity.HasMany(e => e.Inquiries)
                .WithOne(i => i.Agent)
                .HasForeignKey(i => i.AgentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ListingInquiry configuration
        modelBuilder.Entity<ListingInquiry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ListingId);
            entity.HasIndex(e => e.AgentId);
            entity.HasIndex(e => e.CreatedAt);

            entity.Property(e => e.Message).HasMaxLength(2000);
            entity.Property(e => e.BuyerName).HasMaxLength(255);
            entity.Property(e => e.BuyerEmail).HasMaxLength(255);
            entity.Property(e => e.BuyerPhone).HasMaxLength(32);
            entity.Property(e => e.OfferAmount).HasPrecision(18, 2);
        });

        // ListingDistribution configuration
        modelBuilder.Entity<ListingDistribution>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ListingId, e.AgentId }).IsUnique();
            entity.HasIndex(e => e.SentAt);
        });

        // Fido2Credential — passkey storage for WebAuthn registrations.
        modelBuilder.Entity<Fido2Credential>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.CredentialId).IsUnique();
            entity.Property(e => e.CredType).HasMaxLength(64);
            entity.Property(e => e.Nickname).HasMaxLength(120);
        });

        // ProductEvent — privacy-minimised, durable activity facts used for
        // rolling MAU/WAU. No free-text metadata or PII is stored here.
        modelBuilder.Entity<ProductEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.OccurredAt });
            entity.HasIndex(e => new { e.OccurredAt, e.UserId });
            entity.HasIndex(e => new { e.Name, e.OccurredAt });
            entity.HasIndex(e => new { e.ListingId, e.Name, e.OccurredAt });
            entity.Property(e => e.Name).HasMaxLength(80).IsRequired();

            entity.HasOne(e => e.User)
                .WithMany(user => user.ProductEvents)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
