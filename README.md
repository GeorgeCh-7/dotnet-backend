# .NET Backend – Developer Test Project

ASP.NET Core 8 minimal API serving users and tasks data as part of a three-tier architecture:

```
React Frontend (port 5173)
    ↓
Node.js Backend (port 3000) – API Gateway
    ↓
.NET Backend  (port 8080) – Data Source  ← this project
```

## Stack

- .NET 8 / ASP.NET Core Minimal APIs
- Dual storage: in-memory + JSON persistence (default) or SQLite via EF Core (`UseDatabase: true`)
- `IMemoryCache` read cache (30s TTL, in-memory backend)
- API key authentication (`X-API-Key` header, configurable keys)
- Fixed-window rate limiting (configurable per-IP, default 100 req/min)
- In-process metrics collector + `GET /metrics` endpoint
- xUnit test suite — 51 tests (24 unit + 27 integration)

## Project Structure

```
dotnet-backend/
├── Program.cs                    # Middleware pipeline and all route handlers
├── appsettings.json              # Default configuration (API keys, rate limit, DB toggle)
├── Data/
│   ├── IDataStore.cs             # Storage abstraction interface
│   ├── DataStore.cs              # In-memory store + JSON persistence + IMemoryCache
│   ├── DatabaseDataStore.cs      # SQLite / EF Core store (opt-in)
│   └── AppDbContext.cs           # EF Core DbContext with seed data
├── Models/
│   ├── User.cs                   # User entity
│   ├── TaskItem.cs               # Task entity
│   ├── Requests.cs               # Request DTOs + IValidatable interface
│   └── Responses.cs              # Response DTOs
├── Middleware/
│   ├── ApiKeyMiddleware.cs       # X-API-Key header authentication
│   └── ValidationFilter.cs       # Generic endpoint filter – runs IValidatable.Validate()
├── Services/
│   └── MetricsCollector.cs       # Thread-safe request metrics
├── HealthChecks/
│   └── DataStoreHealthCheck.cs   # Custom health check reporting store stats
└── dotnet-backend.Tests/
    ├── DataStoreTests.cs         # Unit tests for DataStore (22 tests)
    └── ApiIntegrationTests.cs    # Integration tests via WebApplicationFactory (29 tests)
```

## Running

```bash
cd dotnet-backend
dotnet run
# Listening on http://localhost:8080
```

Override port or data file:

```bash
PORT=8081 dotnet run
DataFilePath=/tmp/mydata.json dotnet run
```

## Tests

```bash
cd dotnet-backend
dotnet test
```

## API

All endpoints except `/health` and `/swagger` require `X-API-Key: dev-api-key-12345`.

| Method | Path | Description |
|--------|------|-------------|
| GET | `/health` | Health check with store stats |
| GET | `/metrics` | Request metrics since startup |
| GET | `/api/users` | List all users |
| GET | `/api/users/{id}` | Get user by ID |
| POST | `/api/users` | Create user (`name`, `email`, `role`) |
| GET | `/api/tasks` | List tasks, optional `?status=` / `?userId=` filters |
| POST | `/api/tasks` | Create task (`title`, `status`, `userId`) |
| PUT | `/api/tasks/{id}` | Partial update (all fields optional) |
| GET | `/api/stats` | Aggregate user/task counts |

Valid task statuses: `pending`, `in-progress`, `completed`.

## Configuration (`appsettings.json`)

```json
{
  "RequireApiKey": false,
  "ApiKeys": ["dev-api-key-12345"],
  "RateLimitPerMinute": 100,
  "UseDatabase": false,
  "DatabasePath": ""
}
```

**API key auth** is off by default so the existing Node.js gateway and React frontend work without changes. To enable it:

```bash
dotnet run --RequireApiKey=true
```

Then pass the key in every request:

```
X-API-Key: dev-api-key-12345
```

Keys are listed under `ApiKeys` in config. Add or replace entries to rotate keys. `/health` and `/swagger` are always public regardless of this setting.

Set `UseDatabase: true` to switch from JSON persistence to SQLite:

```bash
dotnet run --UseDatabase=true
```

## Design Notes

- **Storage abstraction**: `IDataStore` lets you swap between the in-memory+JSON store and SQLite without touching any endpoint code.
- **Validation split**: format/enum checks live on request DTOs (`IValidatable`) and run via `ValidationFilter<T>` before the handler. Domain checks (user exists, task found) stay in the handler.
- **Caching**: reads are served from `IMemoryCache` (30s TTL); writes immediately remove the affected cache keys.
- **Persistence**: the full snapshot is saved to JSON after every write. On startup the file is loaded; missing or corrupt files fall back to seed data.
- **Metrics**: `MetricsCollector` uses `Interlocked` for lock-free counter updates.
