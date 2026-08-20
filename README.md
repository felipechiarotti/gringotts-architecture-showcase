# Gringotts — Architecture & Security Showcase

> This is a **curated showcase**, not the full source code. Gringotts is a production personal-finance
> app handling real financial data (mine), so the private repository stays private. This document walks
> through the architecture and the security decisions behind it, with real (but isolated) code snippets
> pulled from the actual codebase.

Live app: **[gringotts.com.br](https://gringotts.com.br)**

## What it is

Gringotts is a personal finance API + React frontend: income/expense tracking, categorized budgets and
spending goals, recurring transaction templates, bank statement (OFX/CSV) import with AI-assisted
categorization, and financial forecasting. Built and operated solo — backend, frontend, infra, and
production security, end to end.

## Stack

| Layer | Technology |
|---|---|
| Backend | .NET 10 — ASP.NET Core Minimal API |
| Architecture | Clean/Onion Architecture + CQRS (MediatR) |
| Database | PostgreSQL 16 (EF Core + Npgsql) |
| Cache | Redis 7 |
| Auth | JWT (HS256) + PIN-based two-factor login |
| Validation | FluentValidation (MediatR pipeline behavior) |
| Jobs | Hangfire (PostgreSQL-backed) |
| Logging | Serilog → Seq |
| AI | Anthropic Claude API (transaction categorization) |
| Infra | Docker Compose, Nginx, GitHub Actions, Tailscale, Cloudflare, Uptime Kuma |

## Architecture

```
src/
├── Gringotts.Domain          # Entities, enums, interfaces — zero external dependencies
├── Gringotts.Application     # CQRS commands/queries, validators (depends on Domain)
├── Gringotts.Infrastructure  # EF Core, repositories, jobs, external services
└── Gringotts.Api             # Minimal API endpoints, middleware, composition root
```

Dependencies point inward (Onion Architecture) — the Domain layer knows nothing about EF Core, HTTP,
or any framework. Every feature follows the same CQRS shape: a `Command`/`Query` record, a
`FluentValidation` validator, and a `Handler`, wired together by a `ValidationBehavior` and
`LoggingBehavior` MediatR pipeline that runs automatically before every handler executes.

Domain entities expose only static factory methods (`Entity.Create(...)`, `entity.Update(...)`) — no
public constructors — so an invalid entity state can't be constructed by accident anywhere in the
codebase.

---

## Security decisions

### 1. IDOR prevention as a standing rule

Every handler that loads a resource by ID re-checks resource ownership against the authenticated user
before returning or mutating anything — applied consistently across 30+ handlers (transactions, goals,
payment methods, categories, templates, requests), not bolted on per-feature.

```csharp
// snippets/IdorPrevention.cs — real pattern, used identically across every resource type
public class DeleteGoalCommandHandler(
    ILogger<DeleteGoalCommandHandler> logger,
    IGoalRepository goalRepository,
    ICurrentUserService currentUserService) : IRequestHandler<DeleteGoalCommand, bool>
{
    public async Task<bool> Handle(DeleteGoalCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId;
        var goal = await goalRepository.GetByIdAsync(request.Id, cancellationToken)
                   ?? throw new NotFoundException($"Goal {request.Id} not found");

        if (goal.UserId != userId)
            throw new UnauthorizedException("You do not own this goal.");

        goalRepository.Delete(goal);
        logger.LogInformation("Goal {GoalId} deleted", goal.Id);
        return await goalRepository.SaveChangesAsync(cancellationToken) > 0;
    }
}
```

The ID in the URL is never trusted on its own — a `NotFoundException`/`UnauthorizedException` fires
before any data crosses the boundary back to the caller, and existence isn't leaked either way.

### 2. Password hashing: BCrypt + pepper, layered on purpose

```csharp
// snippets/PasswordHasher.cs
public class PasswordHasher(IOptions<SecuritySettings> options) : IPasswordHasher
{
    private const int WorkFactor = 12;
    private readonly SecuritySettings _securitySettings = options.Value;

    public string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(AddPepper(password), WorkFactor);

    public bool Verify(string password, string hash) =>
        BCrypt.Net.BCrypt.Verify(AddPepper(password), hash);

    private string AddPepper(string password) => password + _securitySettings.PasswordPepper;
}
```

Three independent layers, deliberately stacked: a per-user random salt (BCrypt's own), a shared
server-side secret (the pepper, held only in server config — never in the database), and an adaptive
work factor (12 rounds). A database leak alone isn't enough to crack passwords offline; the pepper has
to leak too, from a completely different place (server config, not the DB).

Login also runs a dummy password-verification pass even when the email doesn't exist, so response
timing can't be used to enumerate which emails are registered.

### 3. JWT: stateless, but scoped tightly

```csharp
// snippets/JwtProvider.cs
var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

var token = new JwtSecurityToken(
    configuration["Jwt:Issuer"],
    configuration["Jwt:Audience"],
    claims,
    expires: DateTime.UtcNow.AddDays(int.Parse(configuration["Jwt:ExpirationInDays"]!)),
    signingCredentials: credentials
);
```

HMAC-SHA256 signing under a dedicated secret key, validated end-to-end (issuer, audience, lifetime,
signature, zero clock skew). Combined with the PIN-based two-factor step on login, a leaked password
alone isn't enough to obtain a session.

### 4. Network exposure: nothing reachable unless it has to be

The base `docker-compose.yml` publishes **no host ports at all** for the API, PostgreSQL, Redis or Seq —
they only talk to each other over the internal Docker network:

```yaml
# docker-compose.yml (base) — no `ports:` on any service
services:
  api:
    build: { context: ., dockerfile: src/Gringotts.Api/Dockerfile }
    networks: [gringotts_net]
  postgres:
    image: postgres:16-alpine
    networks: [gringotts_net]
  redis:
    image: redis:7-alpine
    networks: [gringotts_net]
```

The only ports bound to a host interface live in the dev-only override file, and even there they're
bound to `127.0.0.1` / a `BIND_IP` variable — never `0.0.0.0`:

```yaml
# docker-compose.override.yml — the only place any port touches a host interface
api:
  ports: ["127.0.0.1:15080:8080"]
postgres:
  ports: ["${BIND_IP}:15432:5432"]
redis:
  ports: ["${BIND_IP}:15379:6379"]
```

In production, `BIND_IP` is the machine's Tailscale IP — so Postgres, Redis, Seq and the Hangfire/Uptime
Kuma dashboards are reachable only from inside the Tailscale mesh, never from the public internet. The
only public entry point is Nginx, terminating Cloudflare Origin CA certificates and sitting behind
Cloudflare's proxy/WAF/DDoS layer — so traffic is encrypted end-to-end (client → Cloudflare → Nginx),
not just client-to-edge.

Deploys reach the VPS the same way: the GitHub Actions runner joins the Tailscale network over OAuth
(no long-lived SSH keys) and is only allowed in because of a Tailscale ACL rule scoping `tag:ci` →
`tag:vps`.

### 5. Backups treated as a security control

Daily cron job dumps PostgreSQL, the Nginx config, and the Cloudflare Origin certs themselves, and ships
them off the VPS entirely to a Cloudflare R2 bucket via `rclone` (7-day retention) — so a compromised or
lost VPS doesn't also mean lost data. Recovery is a documented, tested runbook (~15 min RTO). An Uptime
Kuma push monitor acts as a dead-man's switch, alerting if the backup job silently stops running — not
just if it fails outright.

### 6. Everything else, briefly

- **Deny-by-default authorization** — a fallback policy requires authentication on every endpoint unless
  explicitly marked otherwise; a separate Admin-only policy gates privileged routes.
- **Invite-only registration** — no open sign-up; new accounts require an admin-issued invite tied to an
  email.
- **Rate limiting by risk profile** — tighter limits on auth endpoints (login/PIN attempts), looser but
  still-capped limits on the AI-powered bank-import endpoint.
- **Input validation everywhere** — every command/query runs through FluentValidation before it reaches
  a handler; no raw SQL, EF Core LINQ is fully parameterized.
- **Non-root containers** — the API image runs as a non-root user on a minimal, security-patched base
  image.

### 7. Cloudflare as a second, edge-level security layer

Nginx + the app's own auth aren't the only line of defense — Cloudflare sits in front of all public
traffic with its own independent set of controls, so a bypass of one layer still has to clear the other:

- **TLS 1.3 minimum**, Opportunistic Encryption and Automatic HTTPS Rewrites — refuses anything below
  TLS 1.3, and makes sure no request/asset ever silently falls back to plaintext HTTP.
- **Origin Certificate** (Cloudflare-issued, trusted only by Cloudflare) installed on Nginx — the
  Cloudflare↔origin hop is encrypted too, not just the public-facing edge.
- **Client certificates (mTLS): intentionally off.** Auth is handled at the application layer (JWT +
  2FA); mTLS would add operational overhead (cert distribution/rotation) without a matching threat model
  for a single-user personal app.
- **Edge rate limiting on `/login`** (5 requests / 10 seconds → block) as a coarser, network-level
  backstop *in front of* the app's own PIN-based rate limiter — the DB/app never even sees a login flood
  past that threshold.
- **Custom edge rules**: `Only HTTPS` and `Only HTTP/2-3` block any request that isn't modern
  encrypted traffic (the profile of almost all bot/scanner traffic); an `IPs blocked` rule denies a
  manually curated blocklist; `Block Non-BR Traffic` challenges/blocks requests from outside Brazil,
  since the app has no legitimate users outside the country — this alone removes most opportunistic
  internet-wide scanning before it reaches the origin at all.

The result: an attacker has to get through Cloudflare's edge rules *and* Nginx/Tailscale network
isolation *and* the app's own auth/IDOR checks — none of which alone is the single point of failure.

---

## Performance decisions

The app runs on a deliberately small VPS — **2 vCPU / 2GB RAM**, chosen over a cheaper 1 vCPU box
specifically because a single vCPU serializes every concurrent DB call/query behind one core, which
would show up as request queuing under load. With 2 vCPUs, one core can be doing DB I/O while the other
keeps serving requests. Query and index design then does the rest: in practice the app comfortably
handles **~3,000 requests/second** on that box.

**Indexes built around the actual query shapes, not "index everything":**

```csharp
// TransactionConfiguration.cs — composite indexes match how the dashboard/forecast actually filter
builder.HasIndex(t => new { t.UserId, t.Date });
builder.HasIndex(t => new { t.UserId, t.Status });
builder.HasIndex(t => new { t.UserId, t.SubCategoryId });

// Filtered/partial indexes keep the index small by excluding the common "soft-deleted" case
builder.HasIndex(u => u.Email).IsUnique().HasFilter("\"IsDeleted\" = false");
```
A later migration explicitly *dropped* single-column `IsDeleted` indexes in favor of these composite
ones — a deliberate correction once the real query patterns were clear, not a first-try guess.

**Query patterns that keep memory and round-trips low:**
- `.AsNoTracking()` on every read-only query (no change-tracker overhead for data that's never saved
  back).
- `.AsSplitQuery()` on multi-`Include()` queries (e.g. transactions + tags) to avoid a join-driven
  Cartesian product blowing up row counts.
- `ExecuteUpdateAsync(...)` for bulk field changes (e.g. re-assigning a category on many transactions)
  — one SQL `UPDATE`, never a load-mutate-save loop.
- `.Select(...)` projections for lookups that only need a couple of fields (e.g. checking which
  months already have generated transactions), instead of materializing full entities.
- Hard `.Take(limit)` caps on search endpoints, so a broad query can't return unbounded rows.

**Background jobs batch instead of looping DB calls:** the recurring-transaction generator collects all
transactions to create in memory across every active template, then calls `SaveChangesAsync()` **once**
per run — one round trip regardless of how many templates/users are processed — and skips
already-generated months via an in-memory lookup instead of re-querying per month.

**Resource tuning matched to the 2 vCPU box:** Hangfire is configured with a single worker
(`WorkerCount = 1`) and a 5-minute schedule-polling interval, so the background processor doesn't
compete with request-handling threads for CPU; Npgsql's connection pooling is left at sensible defaults
rather than over-provisioned. Redis is wired up but deliberately *not* used on the hot request path —
the dashboard/transaction data is time-sensitive enough that cache invalidation would cost more than the
read it saves, so the design keeps the request path simple (indexed DB read) instead of adding caching
complexity where it wouldn't pay off.

---

## Why this repo exists

Recruiters and engineers reviewing my resume often want to see actual code, not just bullet points. This
repo is the compromise: real patterns and real reasoning, without exposing the production source of an
app that manages real financial data.
