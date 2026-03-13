using Microsoft.EntityFrameworkCore;
using DataVault.API.Models;

namespace DataVault.API.Data;

public class VaultDbContext : DbContext
{
    public VaultDbContext(DbContextOptions<VaultDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<VaultFile> Files => Set<VaultFile>();
    public DbSet<FileVersion> FileVersions => Set<FileVersion>();
    public DbSet<ShareLink> ShareLinks => Set<ShareLink>();
    public DbSet<FilePermission> FilePermissions => Set<FilePermission>();
    public DbSet<AccessLog> AccessLogs => Set<AccessLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User indexes
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email).IsUnique();
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username).IsUnique();

        // VaultFile → Owner
        modelBuilder.Entity<VaultFile>()
            .HasOne(f => f.Owner)
            .WithMany(u => u.Files)
            .HasForeignKey(f => f.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        // ShareLink → File
        modelBuilder.Entity<ShareLink>()
            .HasOne(s => s.File)
            .WithMany(f => f.ShareLinks)
            .HasForeignKey(s => s.FileId)
            .OnDelete(DeleteBehavior.Cascade);

        // ShareLink → CreatedBy
        modelBuilder.Entity<ShareLink>()
            .HasOne(s => s.CreatedBy)
            .WithMany(u => u.SharedLinks)
            .HasForeignKey(s => s.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        // ShareLink token index
        modelBuilder.Entity<ShareLink>()
            .HasIndex(s => s.Token).IsUnique();

        // AccessLog → File (NoAction to avoid cascade cycles)
        modelBuilder.Entity<AccessLog>()
            .HasOne(a => a.File)
            .WithMany(f => f.AccessLogs)
            .HasForeignKey(a => a.FileId)
            .OnDelete(DeleteBehavior.NoAction);

        // AccessLog → User (NoAction to avoid cascade cycles)
        modelBuilder.Entity<AccessLog>()
            .HasOne(a => a.User)
            .WithMany(u => u.AccessLogs)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        // AccessLog → ShareLink (NoAction to avoid cascade cycles)
        modelBuilder.Entity<AccessLog>()
            .HasOne(a => a.ShareLink)
            .WithMany(s => s.AccessLogs)
            .HasForeignKey(a => a.ShareLinkId)
            .OnDelete(DeleteBehavior.NoAction);

        // FileVersion → File
        modelBuilder.Entity<FileVersion>()
            .HasOne(v => v.File)
            .WithMany(f => f.Versions)
            .HasForeignKey(v => v.FileId)
            .OnDelete(DeleteBehavior.Cascade);

        // FilePermission → File
        modelBuilder.Entity<FilePermission>()
            .HasOne(p => p.File)
            .WithMany(f => f.Permissions)
            .HasForeignKey(p => p.FileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
