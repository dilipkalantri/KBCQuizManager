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
    
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        
        // Rename Identity tables with custom prefix
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("Users");
            entity.HasIndex(e => e.Email).IsUnique();
            
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
        });
        
        builder.Entity<IdentityRole<Guid>>(entity =>
        {
            entity.ToTable("Roles");
        });
        
        builder.Entity<IdentityUserRole<Guid>>(entity =>
        {
            entity.ToTable("UserRoles");
        });
        
        builder.Entity<IdentityUserClaim<Guid>>(entity =>
        {
            entity.ToTable("UserClaims");
        });
        
        builder.Entity<IdentityUserLogin<Guid>>(entity =>
        {
            entity.ToTable("UserLogins");
        });
        
        builder.Entity<IdentityRoleClaim<Guid>>(entity =>
        {
            entity.ToTable("RoleClaims");
        });
        
        builder.Entity<IdentityUserToken<Guid>>(entity =>
        {
            entity.ToTable("UserTokens");
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
            entity.HasIndex(e => e.Difficulty);
        });
    }
}
