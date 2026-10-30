# Mini Online Shop

A small part of an online shop:

- **Back end:** a .NET 10 Web API with minimal APIs, EF Core 10 and Microsoft SQL Server 2022.
- **Front end:** native Web Components with no build step.

```
BackEnd/    .NET solution (OnlineShop.slnx): Domain / Application / Infrastructure / Api + tests, Dockerfile
FrontEnd/   Static Web Components app (index.html, src/), nginx.conf for Docker
docker-compose.yml   SQL Server + API + front end
```

## Summary

### Key technical decisions

- **Clean Architecture.** Four projects: Domain → Application → Infrastructure / Api, with dependencies pointing inwards. Architecture tests (NetArchTest) fail the build if a layer reaches outwards.
- **Rich domain model.** The `Order` aggregate owns pricing, coupon rules, stock reservation and status changes. Use cases only load data, call the domain and save.
- **Server-side pricing only.** Clients send product ids and quantities, never prices. Every change re-prices the draft from the catalogue, and SQL check constraints back the main money rules.
- **Optimistic concurrency** on products and orders (`Version` token) with an automatic retry, so two buyers can never take the last unit. I verified it with 10 parallel requests against one unit in stock: exactly one succeeds and nine get 409.
- **Background job** (`AbandonedDraftSweeper`): a `BackgroundService` that only schedules. The expiry logic is a use case, it is configurable, every run is logged, and each order is saved on its own so one conflict does not roll back the others.
- **Infrastructure:** SQL Server with EF Core migrations and an idempotent seed. Docker Compose runs the whole stack with one command.
- **Front end:** native Web Components with Shadow DOM and no build step. A small `ApiClient` and `SessionStore` handle the API and the token, and components talk to each other through `CustomEvent`s.

### Assumptions, trade-offs and planning

- **Planning:** I picked the abandoned-draft sweeper as the background job, because it fits stock reservation: drafts hold stock, and the sweeper returns it. I built the domain and API first, then the UI, then the tests, then hardening and the refactor.
- **Assumptions:**
  - One seeded user is enough, so there is no registration.
  - The product catalogue is read-only.
  - Deleting an order is a soft delete that releases its stock.
  - A placed order is locked.
- **Coupons:** the brief asks for coupons but does not define their rules, so I chose them:
  - a coupon is either a percentage or a fixed amount;
  - it can have a minimum subtotal and a validity window;
  - the discount never exceeds the subtotal;
  - an order has at most one coupon.

- **Next steps with more time:**
  - **User registration and full user management:** sign-up, email confirmation, password reset, profile editing, and deactivating or deleting users;
  - **User roles:** `Customer` and `Admin`, with role-based authorization policies on the endpoints;
  - **Admin panel:** product and stock management, coupon management, and an overview of all orders and users;
  - **Background job logging:** a persisted history of every sweeper run (start, duration, expired drafts, errors), visible in the admin panel;
  - **Error and audit logging:**
    - structured logs with Serilog to a central store such as Seq or Elasticsearch;
    - an audit log table recording who created, changed, placed or deleted each order, and when;
  - **Sales reports and statistics:** revenue per day or month, best-selling products, coupon usage and average order value;
  - front-end component and end-to-end tests;
  - `ETag`/`If-Match` on order updates;
  - an outbox with domain events (for example a confirmation email);
  - OpenTelemetry;
  - a CI pipeline.

### Challenges and how I addressed them

- **EF Core and domain-generated GUIDs.** A new order line with a pre-set id was treated as an existing row (an `UPDATE` that affected 0 rows). I fixed this with `ValueGeneratedNever()` on all keys.
- **Changing database mid-way, from PostgreSQL to SQL Server.** I swapped the provider, rewrote the check constraints in T-SQL, regenerated the migration and moved the integration tests to a SQL Server Testcontainer. The SQL Server container uses host port 14330 so it does not clash with a local SQL Server instance.
- **Testing concurrency for real.** The live race test exposed that a malformed request body returned 500 instead of 400. I mapped `BadHttpRequestException` to a problem-details 400 in every environment.
- **Background job talking to the database directly.** Its logic sat in the API project and used `DbContext`. I extracted an Application layer with ports, so the job and endpoints only call use cases, and added architecture tests that enforce this.
- **Front-end state issues:**
  - Re-rendering the whole product list on every quantity change stole input focus. I now render the table and the order panel separately.
  - Fast sort or page changes could show an older response last. I now cancel stale requests with `AbortController`.
- **Small fixes:**
  - nginx serves files with `no-cache` so changes show up without a hard refresh.
  - I pinned a vulnerable transitive package (`System.Security.Cryptography.Pkcs`).

### How AI was used

- **Tool and division of work:** I used Claude Code (an AI coding agent) throughout. It generated most of the code, tests, Docker setup and this README from my instructions. I directed the design, reviewed the output and changed it where it did not match what I wanted.
- **Corrections and changes I made to the AI output:**
  - **Structure:** I asked for a `BackEnd/` and `FrontEnd/` split, and for SQL Server instead of the PostgreSQL it first chose.
  - **Architecture:** in review I noticed that the background job accessed the database directly, which led to the Clean Architecture refactor. I moved the request and response contracts into the Application `Abstractions` namespace myself, and the architecture test was then narrowed to forbid only the ports.
- **Verification:**
  - Every change was checked by running the full test suite.
  - API behaviour was smoke-tested against the running Docker stack.
  - The UI flow was clicked through in a browser.
  - I reviewed the code before committing.

## Prerequisites

- Docker Desktop, used for the full stack, for the database in local development, and by the integration tests.
- .NET SDK 10.0.400 or later. You only need it to run the API outside Docker or to run the tests.

## Run everything with Docker (recommended)

```bash
docker compose up -d --build
```

| What | URL |
|---|---|
| Front end | http://localhost:8080 |
| API | http://localhost:5080 (health: `/health`) |
| Swagger UI | http://localhost:5080/swagger (OpenAPI document: `/openapi/v1.json`) |
| SQL Server | `localhost,14330`, database `Shop`, login `sa` / `Shop_Passw0rd!` (demo-only credentials) |

On startup the API applies the EF Core migrations and seeds the data. Both steps are idempotent.

Stop the stack with `docker compose down`, or with `docker compose down -v` to also delete the database.

## Run the API locally (without the API container)

```bash
docker compose up -d db web
dotnet run --project BackEnd/src/Shop.Api
```

The API listens on http://localhost:5080. The front end stays at http://localhost:8080, and `FrontEnd/config.js` points the browser at port 5080. If you would rather not use nginx, any static file server works. For example, `npx http-server FrontEnd -p 5500` serves it on port 5500, and the API allows that origin through CORS.

## Seeded data

| | |
|---|---|
| User | `demo@shop.test` / `Demo123!` |
| Products | 13 products in 8 categories; `SPK-BT-MINI` is out of stock |
| Coupons | `WELCOME10`: 10% off<br>`SAVE20`: 20.00 off when the subtotal is at least 100.00<br>`SPRING2024`: expired, to show the validation error |

## Tests

```bash
dotnet test BackEnd/OnlineShop.slnx
```

| Suite | Tests | What it covers | Result |
|---|---|---|---|
| Domain (`tests/Shop.Tests/Domain`) | 14 | Totals from catalogue prices, rounding, coupon rules (percentage, fixed cap, minimum, expiry), stock reserve/release, quantity limits, locked placed orders | ✅ 14 passed |
| Application (`tests/Shop.Tests/Application`) | 6 | Use cases with in-memory fakes: pricing and coupons, retry after a concurrency conflict, giving up after 3 conflicts, unknown product or coupon, other user's order, draft sweeper skipping a conflicting order | ✅ 6 passed |
| Architecture (`tests/Shop.Tests/Architecture`) | 6 | Dependency rule between layers; endpoints and the background job only use use cases, and only through their interfaces; every use case has an interface; a control check that the rules detect real dependencies | ✅ 6 passed |
| Integration (`tests/Shop.Tests/Integration`) | 22 | Real API against SQL Server in Testcontainers: login, 401 without a token, paging and sorting, 400 on invalid input or malformed JSON, client prices ignored, 409 on insufficient stock, merge and replace updates, coupons, placed orders locked, soft delete releasing stock, user isolation, sweeper, idempotent seeding | ✅ 22 passed |
| **Total** | **48** | | **✅ 48 passed, 0 failed** |

The integration tests need Docker running. The first run pulls the SQL Server image, which takes a few minutes.

**Manual checks** against the running Docker stack:
- **Concurrency:** 10 parallel orders for 1 unit in stock gave exactly 1 × 201 and 9 × 409. Stock ended at 0, and the logs show the retries.
- **UI:** the full flow was clicked through in a browser: login, picking quantities, creating an order with a coupon, applying and removing coupons (including an expired one), changing quantities and deleting the order.

## API

All `/orders` endpoints need `Authorization: Bearer <token>` and only work on the caller's own orders. Another user's order returns 404, the same as a missing one.

| Method | Path | Purpose |
|---|---|---|
| POST | `/auth/login` | `{ email, password }` → `{ accessToken, expiresAt, ... }` |
| GET | `/products?page=&pageSize=&sortBy=&sortDirection=&category=` | Paged list.<br>`sortBy`: `name`, `price`, `sku`, `category`, `stockQuantity` or `createdAt`.<br>`sortDirection`: `asc` or `desc` |
| GET | `/orders?page=&pageSize=` | The caller's orders, newest first |
| POST | `/orders` | `{ items: [{ productId, quantity }], couponCode? }` → 201 |
| GET | `/orders/{id}` | The order with its lines and computed totals |
| PATCH | `/orders/{id}` | `{ items?, itemsMode?: "Merge"\|"Replace", couponCode?, removeCoupon? }` |
| POST | `/orders/{id}/place` | Locks a draft (extra, not in the brief) |
| DELETE | `/orders/{id}` | Soft delete; releases reserved stock → 204 |

How `PATCH` treats items:
- In `Merge` mode (the default), each line in `items` adds a product or changes its quantity. Quantity `0` removes the line.
- In `Replace` mode, `items` becomes the complete set of lines.

Errors are RFC 7807 problem details with a stable `code`, for example `coupon.minimum_not_met` or `product.insufficient_stock`:

| Status | Meaning |
|---|---|
| 400 | Invalid input or a broken business rule |
| 404 | The order does not exist or is not yours |
| 409 | Not enough stock, the order is no longer editable, or a concurrency conflict |

**Swagger UI** at http://localhost:5080/swagger lists every endpoint:
1. Call `POST /auth/login`.
2. Click **Authorize** and paste the returned `accessToken`.

The order endpoints are marked with a lock and send the token automatically.

**CORS** allows the front-end origins listed in `Cors:AllowedOrigins` in `appsettings.json`: `http://localhost:8080`, `http://localhost:5500` and `http://127.0.0.1:5500`. Other origins get no CORS headers, so browsers block them. Override the list with the environment variables `Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1` and so on.

Example requests are in [`BackEnd/src/Shop.Api/Shop.Api.http`](BackEnd/src/Shop.Api/Shop.Api.http). They work with the VS Code REST Client, Visual Studio or Rider. The login token is captured automatically.

### Stock

- A draft reserves stock. A quantity change reserves or releases only the difference.
- Deleting or expiring an order releases its stock.
- Products have an optimistic concurrency token, so two orders cannot take the same units. On a conflict the service retries with fresh data up to 3 times before it returns 409.

### Background job: abandoned-draft sweeper

`AbandonedDraftSweeper` is a `BackgroundService` driven by a `PeriodicTimer`. It only schedules: on each tick it calls the `ExpireAbandonedDraftsUseCase` in the Application layer. That use case finds drafts that have not been touched for longer than `DraftTimeout`, marks them `Expired` and releases their stock. The job never touches the database itself.

- **Batching.** Each run handles at most `BatchSize` drafts.
- **Independent saves.** Each order is saved separately, so a conflict with a customer editing the same draft only skips that one order.
- **Logging.** Every run is logged.

The settings live under `DraftSweeper` (environment variables use `DraftSweeper__Interval` and so on):

```json
"DraftSweeper": { "Enabled": true, "Interval": "00:01:00", "DraftTimeout": "00:30:00", "BatchSize": 100 }
```

## Architecture

The back end follows Clean Architecture: dependencies point only inwards, toward the domain.

```
Shop.Api ──────────────┐        endpoints, background job host, JWT validation, composition root
   │                   ▼
   │          Shop.Infrastructure   EF Core, repositories, queries, JWT issuing, password hashing
   ▼                   │ implements
Shop.Application ◄─────┘        use cases + ports (interfaces)
   │
   ▼
Shop.Domain                     entities + business rules, no dependencies
```

- **`Shop.Domain`** has no dependencies. It holds the entities with private setters and behaviour methods, and an `Order` aggregate that owns its lines, pricing, coupon rules and stock reservation. Invariants are enforced here.
- **`Shop.Application`** holds one class per use case, each behind its own interface (for example `ICreateOrderUseCase`). Endpoints and the background job depend only on the interfaces:

  | Area | Use cases |
  |---|---|
  | Auth | `LoginUseCase` |
  | Products | `ListProductsUseCase` |
  | Orders | `ListOrdersUseCase`, `GetOrderUseCase`, `CreateOrderUseCase`, `UpdateOrderUseCase`, `PlaceOrderUseCase`, `DeleteOrderUseCase` |
  | Background | `ExpireAbandonedDraftsUseCase` |

  - **Ports.** It defines the interfaces it needs: `IOrderRepository`, `IProductRepository`, `ICouponRepository`, `IUserRepository`, `IUnitOfWork`, `IProductQueries`, `IOrderQueries`, `IPasswordHasher` and `ITokenService`.
  - **Shared rules.** Ownership checks and retry on a concurrency conflict live here.
  - **No framework dependencies.** It does not reference EF Core, ASP.NET or JWT libraries.
- **`Shop.Infrastructure`** implements those ports.
  - **EF Core:** the `DbContext`, the Fluent configurations (check constraints, indexes, a soft-delete query filter), the repositories, the unit of work and the migrations. The unit of work turns EF's concurrency exception into the application's `ConcurrencyException`.
  - **Read-side queries:** product and order lists are read without loading the aggregate.
  - **Security:** JWT issuing and password hashing.
  - **Seeding:** the idempotent seeder.
- **`Shop.Api`** is the outer shell: minimal API endpoints that only call use cases, the background job that only schedules a use case, JWT validation, the exception-to-ProblemDetails mapping, and the composition root that wires everything together.
- **Architecture tests** (`tests/Shop.Tests/Architecture`, using NetArchTest) fail the build if these rules break:
  - the Domain depends on anything else;
  - the Application depends on EF Core, ASP.NET or Infrastructure;
  - the Infrastructure depends on the Api;
  - endpoints or background jobs reach past the use cases.
- **`FrontEnd`** contains the Web Components:

  | Component | Role |
  |---|---|
  | `shop-app` | Root: hash routing, layout, logout |
  | `login-form` | Sign-in form |
  | `product-catalog` | Paging, sorting, picking quantities, creating an order |
  | `order-list` | The user's orders |
  | `order-detail` | Edit quantities, apply or remove a coupon, place or delete the order |

  - **Shared state and API.** `SessionStore` holds the token in `sessionStorage` and raises change events. `ApiClient` wraps `fetch`, attaches the token and turns problem details into errors. On a 401 it clears the session.
  - **Component boundaries.** Each component uses Shadow DOM. Components talk to each other through bubbling `CustomEvent`s (`order-created`, `order-selected`, `order-deleted`), and the root turns those into navigation.

