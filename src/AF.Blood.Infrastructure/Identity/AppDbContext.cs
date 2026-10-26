using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AF.Blood.Infrastructure.Identity;

/// <summary>ASP.NET Core Identity application user. Custom columns map to the
/// Flyway-created schema (V001) — see ADR-0003.</summary>
public class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public class AppRole : IdentityRole<Guid>
{
    public string? Description { get; set; }
}

/// <summary>EF Core is used ONLY for Identity persistence (spec §3). The database
/// schema is owned by Flyway; no EF migrations exist for this context.</summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(entity =>
        {
            entity.ToTable("AspNetUsers");
            entity.Property(u => u.DisplayName).HasMaxLength(128).IsRequired();
            entity.Property(u => u.CreatedAtUtc).HasDefaultValueSql("now()");
            entity.Property(u => u.UpdatedAtUtc).HasDefaultValueSql("now()");
        });

        builder.Entity<AppRole>(entity =>
        {
            entity.ToTable("AspNetRoles");
            entity.Property(r => r.Description);
        });
    }
}
