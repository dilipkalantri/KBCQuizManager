using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using KBCQuizManager.Web.Data.Entities;

namespace KBCQuizManager.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }
    
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<GameSession> GameSessions => Set<GameSession>();
    public DbSet<GameSessionAnswer> GameSessionAnswers => Set<GameSessionAnswer>();
    public DbSet<PublicUser> PublicUsers => Set<PublicUser>();
    
    // Multiplayer
    public DbSet<MultiplayerGame> MultiplayerGames => Set<MultiplayerGame>();
    public DbSet<MultiplayerPlayer> MultiplayerPlayers => Set<MultiplayerPlayer>();
    public DbSet<MultiplayerAnswer> MultiplayerAnswers => Set<MultiplayerAnswer>();
    
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        
        // Rename Identity tables with custom prefix
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("Users");
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.AdminCode).IsUnique().HasFilter(@"""AdminCode"" IS NOT NULL");
            
            entity.HasMany(u => u.Categories)
                .WithOne(c => c.Owner)
                .HasForeignKey(c => c.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
                
            entity.HasMany(u => u.Questions)
                .WithOne(q => q.Owner)
                .HasForeignKey(q => q.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
                
            entity.HasOne(u => u.CreatedBy)
                .WithMany()
                .HasForeignKey(u => u.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);
                
            entity.HasMany(u => u.PublicUsers)
                .WithOne(p => p.Admin)
                .HasForeignKey(p => p.AdminId)
                .OnDelete(DeleteBehavior.Cascade);
                
            entity.HasOne(u => u.LinkedAdmin)
                .WithMany(u => u.LinkedPlayers)
                .HasForeignKey(u => u.LinkedAdminId)
                .OnDelete(DeleteBehavior.SetNull);
        });
        
        builder.Entity<IdentityRole<Guid>>(entity => { entity.ToTable("Roles"); });
        builder.Entity<IdentityUserRole<Guid>>(entity => { entity.ToTable("UserRoles"); });
        builder.Entity<IdentityUserClaim<Guid>>(entity => { entity.ToTable("UserClaims"); });
        builder.Entity<IdentityUserLogin<Guid>>(entity => { entity.ToTable("UserLogins"); });
        builder.Entity<IdentityRoleClaim<Guid>>(entity => { entity.ToTable("RoleClaims"); });
        builder.Entity<IdentityUserToken<Guid>>(entity => { entity.ToTable("UserTokens"); });
        
        // PublicUser configuration
        builder.Entity<PublicUser>(entity =>
        {
            entity.ToTable("PublicUsers");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AdminId);
            entity.HasIndex(e => new { e.AdminId, e.Name });
        });
        
        // Category configuration
        builder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.OwnerId, e.Name }).IsUnique();
            
            entity.HasMany(c => c.Questions)
                .WithOne(q => q.Category)
                .HasForeignKey(q => q.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        
        // Question configuration
        builder.Entity<Question>(entity =>
        {
            entity.ToTable("Questions");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.OwnerId);
            entity.HasIndex(e => e.CategoryId);
            entity.HasIndex(e => e.Level);
        });
        
        // GameSession configuration
        builder.Entity<GameSession>(entity =>
        {
            entity.ToTable("GameSessions");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.OwnerId);
            entity.HasIndex(e => e.StartedAt);
            entity.HasIndex(e => e.Status);
            
            entity.HasOne(g => g.Owner)
                .WithMany()
                .HasForeignKey(g => g.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasOne(g => g.Player)
                .WithMany()
                .HasForeignKey(g => g.PlayerId)
                .OnDelete(DeleteBehavior.SetNull);
                
            entity.HasMany(g => g.Answers)
                .WithOne(a => a.GameSession)
                .HasForeignKey(a => a.GameSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        
        // GameSessionAnswer configuration
        builder.Entity<GameSessionAnswer>(entity =>
        {
            entity.ToTable("GameSessionAnswers");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.GameSessionId);
            
            entity.HasOne(a => a.Question)
                .WithMany()
                .HasForeignKey(a => a.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        
        // Multiplayer Game configuration
        builder.Entity<MultiplayerGame>(entity =>
        {
            entity.ToTable("MultiplayerGames");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.RoomCode).IsUnique();
            entity.HasIndex(e => e.HostId);
            entity.HasIndex(e => e.Status);
            
            entity.HasOne(g => g.Host)
                .WithMany()
                .HasForeignKey(g => g.HostId)
                .OnDelete(DeleteBehavior.Cascade);
                
            entity.HasMany(g => g.Players)
                .WithOne(p => p.Game)
                .HasForeignKey(p => p.GameId)
                .OnDelete(DeleteBehavior.Cascade);
                
            entity.HasMany(g => g.Answers)
                .WithOne(a => a.Game)
                .HasForeignKey(a => a.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        
        // Multiplayer Player configuration
        builder.Entity<MultiplayerPlayer>(entity =>
        {
            entity.ToTable("MultiplayerPlayers");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.GameId);
            entity.HasIndex(e => new { e.GameId, e.PlayerName }).IsUnique();
        });
        
        // Multiplayer Answer configuration
        builder.Entity<MultiplayerAnswer>(entity =>
        {
            entity.ToTable("MultiplayerAnswers");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.GameId);
            entity.HasIndex(e => e.PlayerId);
            entity.HasIndex(e => new { e.GameId, e.PlayerId, e.QuestionIndex }).IsUnique();
            
            entity.HasOne(a => a.Player)
                .WithMany()
                .HasForeignKey(a => a.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
                
            entity.HasOne(a => a.Question)
                .WithMany()
                .HasForeignKey(a => a.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
