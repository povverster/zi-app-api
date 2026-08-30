using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using ZiApp.Domain.Accounts;
using ZiApp.Infrastructure.Identity;

namespace ZiApp.Infrastructure.Persistence;

internal static class IdentityModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureUsers(modelBuilder);
        ConfigureRoles(modelBuilder);
        ConfigureUserClaims(modelBuilder);
        ConfigureUserLogins(modelBuilder);
        ConfigureUserTokens(modelBuilder);
        ConfigureUserRoles(modelBuilder);
        ConfigureRoleClaims(modelBuilder);
    }

    private static void ConfigureUsers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationUser>(builder =>
        {
            builder.ToTable("auth_users");
            builder.HasKey(user => user.Id).HasName("pk_auth_users");

            builder.Property(user => user.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(user => user.AccountId).HasColumnName("account_id").IsRequired();
            builder.Property(user => user.UserName).HasColumnName("user_name").HasMaxLength(256);
            builder.Property(user => user.NormalizedUserName).HasColumnName("normalized_user_name").HasMaxLength(256);
            builder.Property(user => user.Email).HasColumnName("email").HasMaxLength(256);
            builder.Property(user => user.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(256);
            builder.Property(user => user.EmailConfirmed).HasColumnName("email_confirmed");
            builder.Property(user => user.PasswordHash).HasColumnName("password_hash");
            builder.Property(user => user.SecurityStamp).HasColumnName("security_stamp");
            builder.Property(user => user.ConcurrencyStamp).HasColumnName("concurrency_stamp").IsConcurrencyToken();
            builder.Property(user => user.PhoneNumber).HasColumnName("phone_number");
            builder.Property(user => user.PhoneNumberConfirmed).HasColumnName("phone_number_confirmed");
            builder.Property(user => user.TwoFactorEnabled).HasColumnName("two_factor_enabled");
            builder.Property(user => user.LockoutEnd).HasColumnName("lockout_end");
            builder.Property(user => user.LockoutEnabled).HasColumnName("lockout_enabled");
            builder.Property(user => user.AccessFailedCount).HasColumnName("access_failed_count");

            builder.HasOne(user => user.Account)
                .WithOne()
                .HasForeignKey<ApplicationUser>(user => user.AccountId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_auth_users_user_accounts_account_id");

            builder.HasIndex(user => user.AccountId)
                .IsUnique()
                .HasDatabaseName("ux_auth_users_account_id");
            builder.HasIndex(user => user.NormalizedEmail)
                .HasDatabaseName("ix_auth_users_normalized_email");
            builder.HasIndex(user => user.NormalizedUserName)
                .IsUnique()
                .HasFilter("normalized_user_name IS NOT NULL")
                .HasDatabaseName("ux_auth_users_normalized_user_name");
        });
    }

    private static void ConfigureRoles(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityRole<Guid>>(builder =>
        {
            builder.ToTable("auth_roles");
            builder.HasKey(role => role.Id).HasName("pk_auth_roles");
            builder.Property(role => role.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(role => role.Name).HasColumnName("name").HasMaxLength(256);
            builder.Property(role => role.NormalizedName).HasColumnName("normalized_name").HasMaxLength(256);
            builder.Property(role => role.ConcurrencyStamp).HasColumnName("concurrency_stamp").IsConcurrencyToken();
            builder.HasIndex(role => role.NormalizedName)
                .IsUnique()
                .HasFilter("normalized_name IS NOT NULL")
                .HasDatabaseName("ux_auth_roles_normalized_name");

            builder.HasData(
                new IdentityRole<Guid>
                {
                    Id = IdentityRoleIds.User,
                    Name = nameof(AccountRole.User),
                    NormalizedName = nameof(AccountRole.User).ToUpperInvariant(),
                    ConcurrencyStamp = IdentityRoleIds.UserConcurrencyStamp,
                },
                new IdentityRole<Guid>
                {
                    Id = IdentityRoleIds.SuperAdmin,
                    Name = nameof(AccountRole.SuperAdmin),
                    NormalizedName = nameof(AccountRole.SuperAdmin).ToUpperInvariant(),
                    ConcurrencyStamp = IdentityRoleIds.SuperAdminConcurrencyStamp,
                });
        });
    }

    private static void ConfigureUserClaims(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityUserClaim<Guid>>(builder =>
        {
            builder.ToTable("auth_user_claims");
            builder.HasKey(claim => claim.Id).HasName("pk_auth_user_claims");
            builder.Property(claim => claim.Id).HasColumnName("id");
            builder.Property(claim => claim.UserId).HasColumnName("user_id");
            builder.Property(claim => claim.ClaimType).HasColumnName("claim_type");
            builder.Property(claim => claim.ClaimValue).HasColumnName("claim_value");
            builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(claim => claim.UserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_auth_user_claims_auth_users_user_id");
            builder.HasIndex(claim => claim.UserId).HasDatabaseName("ix_auth_user_claims_user_id");
        });
    }

    private static void ConfigureUserLogins(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityUserLogin<Guid>>(builder =>
        {
            builder.ToTable("auth_user_logins");
            builder.HasKey(login => new { login.LoginProvider, login.ProviderKey }).HasName("pk_auth_user_logins");
            builder.Property(login => login.LoginProvider).HasColumnName("login_provider").HasMaxLength(128);
            builder.Property(login => login.ProviderKey).HasColumnName("provider_key").HasMaxLength(128);
            builder.Property(login => login.ProviderDisplayName).HasColumnName("provider_display_name");
            builder.Property(login => login.UserId).HasColumnName("user_id");
            builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(login => login.UserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_auth_user_logins_auth_users_user_id");
            builder.HasIndex(login => login.UserId).HasDatabaseName("ix_auth_user_logins_user_id");
        });
    }

    private static void ConfigureUserTokens(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityUserToken<Guid>>(builder =>
        {
            builder.ToTable("auth_user_tokens");
            builder.HasKey(token => new { token.UserId, token.LoginProvider, token.Name }).HasName("pk_auth_user_tokens");
            builder.Property(token => token.UserId).HasColumnName("user_id");
            builder.Property(token => token.LoginProvider).HasColumnName("login_provider").HasMaxLength(128);
            builder.Property(token => token.Name).HasColumnName("name").HasMaxLength(128);
            builder.Property(token => token.Value).HasColumnName("value");
            builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_auth_user_tokens_auth_users_user_id");
        });
    }

    private static void ConfigureUserRoles(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityUserRole<Guid>>(builder =>
        {
            builder.ToTable("auth_user_roles");
            builder.HasKey(item => new { item.UserId, item.RoleId }).HasName("pk_auth_user_roles");
            builder.Property(item => item.UserId).HasColumnName("user_id");
            builder.Property(item => item.RoleId).HasColumnName("role_id");
            builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.UserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_auth_user_roles_auth_users_user_id");
            builder.HasOne<IdentityRole<Guid>>().WithMany().HasForeignKey(item => item.RoleId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_auth_user_roles_auth_roles_role_id");
            builder.HasIndex(item => item.RoleId).HasDatabaseName("ix_auth_user_roles_role_id");
        });
    }

    private static void ConfigureRoleClaims(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityRoleClaim<Guid>>(builder =>
        {
            builder.ToTable("auth_role_claims");
            builder.HasKey(claim => claim.Id).HasName("pk_auth_role_claims");
            builder.Property(claim => claim.Id).HasColumnName("id");
            builder.Property(claim => claim.RoleId).HasColumnName("role_id");
            builder.Property(claim => claim.ClaimType).HasColumnName("claim_type");
            builder.Property(claim => claim.ClaimValue).HasColumnName("claim_value");
            builder.HasOne<IdentityRole<Guid>>().WithMany().HasForeignKey(claim => claim.RoleId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_auth_role_claims_auth_roles_role_id");
            builder.HasIndex(claim => claim.RoleId).HasDatabaseName("ix_auth_role_claims_role_id");
        });
    }
}