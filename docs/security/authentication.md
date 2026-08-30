# Authentication and account provisioning

ZiApp uses ASP.NET Core Identity backed by PostgreSQL. Identity credentials are
stored in dedicated `auth_*` tables and linked one-to-one with the domain
`user_accounts` table.

## Account policy

- There is no public registration endpoint.
- Only a user in the `SuperAdmin` role can call `POST /api/admin/accounts`.
- A super administrator can create either a regular user or another super
  administrator.
- Email addresses are unique.
- New accounts are active immediately; email confirmation and password reset are
  not part of this stage.
- Five failed login attempts lock an account for 15 minutes.
- Passwords require at least 12 characters, uppercase and lowercase letters, a
  digit, and a non-alphanumeric character.

## Create the first super administrator

Apply database migrations before enabling the bootstrap account:

```powershell
dotnet ef database update `
  --project src/ZiApp.Infrastructure `
  --startup-project src/ZiApp.Api
```

Set the bootstrap values only in the process environment. Do not put the password
in `appsettings.json`, `.env.example`, source control, or deployment logs.

```powershell
$env:BootstrapAdmin__Enabled = "true"
$env:BootstrapAdmin__Email = "admin@example.com"
$env:BootstrapAdmin__DisplayName = "Super Administrator"
$env:BootstrapAdmin__Password = "replace-with-a-strong-password"
$env:BootstrapAdmin__PreferredLanguage = "English"
dotnet run --project src/ZiApp.Api --urls http://localhost:5050
```

The bootstrap service creates an account only when no super administrator exists.
After the first account has been created, stop the API and remove or disable the
bootstrap environment values.

## Browser authentication flow

The React application will use the API through the same site behind the reverse
proxy. Authentication therefore uses an HTTP-only cookie instead of exposing a
token to browser JavaScript.

1. Call `GET /api/auth/csrf` and read `token` from the JSON response.
2. Send the token in the `X-CSRF-TOKEN` header when calling
   `POST /api/auth/login`.
3. Allow the browser to store and send the `ziapp.auth` cookie.
4. Call `GET /api/auth/me` to restore the signed-in account after a page reload.
5. Fetch a fresh CSRF token after login and use it for state-changing requests,
   including account creation and logout.
6. Call `POST /api/auth/logout` with the CSRF header to end the session.

Example login body:

```json
{
  "email": "admin@example.com",
  "password": "replace-with-a-strong-password"
}
```

Example account-creation body:

```json
{
  "email": "investor@example.com",
  "displayName": "Investor",
  "password": "replace-with-a-strong-password",
  "role": "User",
  "preferredLanguage": "Ukrainian"
}
```

Supported role values are `User` and `SuperAdmin`. Supported language values are
`English`, `Ukrainian`, and `Russian`.

## Cookie and CSRF defaults

- The authentication and antiforgery cookies are HTTP-only and `SameSite=Strict`.
- Production cookies require HTTPS.
- The authentication session lasts eight hours and uses sliding expiration.
- API authorization failures return status `401` or `403`, not HTML redirects.
- Login, logout, and account creation validate the antiforgery header.

Before running multiple production API replicas, the infrastructure stage must
persist and share ASP.NET Core Data Protection keys. Until that is configured, an
API restart can invalidate existing login cookies.
