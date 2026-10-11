using Cinerama.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cinerama.Infrastructure.Persistence.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("Roles");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(20).IsRequired();
        b.HasIndex(x => x.Name).IsUnique().HasDatabaseName("UX_Roles_Name");

        b.HasData(
            new Role { Id = Roles.AdminId, Name = Roles.Admin },
            new Role { Id = Roles.CustomerId, Name = Roles.Customer });
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).HasMaxLength(256).IsRequired();
        b.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
        b.Property(x => x.FullName).HasMaxLength(100).IsRequired();
        b.Property(x => x.AvatarUrl).HasMaxLength(500);

        // RN-01: el email ya viene normalizado, así que el índice único aplica la regla sin funciones en la consulta.
        b.HasIndex(x => x.Email).IsUnique().HasDatabaseName("UX_Users_Email");

        b.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshTokens");
        b.HasKey(x => x.Id);

        // SHA-256 = 32 bytes. Nunca se guarda el token en claro (ADR-007).
        b.Property(x => x.TokenHash).HasColumnType("binary(32)").IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("UX_RefreshTokens_TokenHash");
        b.HasIndex(x => x.UserId).HasDatabaseName("IX_RefreshTokens_UserId");
        b.HasIndex(x => x.FamilyId).HasDatabaseName("IX_RefreshTokens_FamilyId");

        b.HasOne(x => x.User).WithMany(u => u.RefreshTokens).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<RefreshToken>().WithMany().HasForeignKey(x => x.ReplacedById).OnDelete(DeleteBehavior.NoAction);
    }
}
