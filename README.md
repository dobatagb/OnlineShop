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

- **Clean Architecture.** Four projects: Domain → Application → Infrastructure / Api, with dependencies pointing inwards. Architecture tests (NetArchTest) fail the test run if a layer reaches outwards.
- **Rich domain model.** The `Order` aggregate owns pricing, coupon rules, stock reservation and status changes. Use cases only load data, call the domain and save.
- **Server-side pricing only.** Clients send product ids and quantities, never prices. Every change re-prices the draft from the catalogue, and SQL check constraints back the main money rules.
- **Stock reservation with optimistic concurrency.** A draft reserves stock, and changing a quantity reserves or releases only the difference. Products and orders carry a `Version` token. On a conflict the use case retries with fresh data, up to 3 attempts, before returning 409. I verified it with 10 parallel requests against one unit in stock: exactly one succeeds and nine get 409.
- **Background job:** an abandoned-draft sweeper. See [Background job](#background-job-abandoned-draft-sweeper).
- **Infrastructure:** SQL Server with EF Core migrations and an idempotent seed. Docker Compose runs the whole stack with one command.

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
- **Trade-offs:**
  - stock is reserved at draft time, which prevents overselling but makes abandoned carts hold stock (hence the sweeper);
  - use cases are plain classes behind interfaces, without MediatR;
  - rule violations are typed exceptions mapped to problem details, rather than a `Result<T>` type;
  - migrations run on startup to keep setup to one command;
  - UUIDv7 keys are generated in the domain, but they are not sequential in a SQL Server clustered index.
- **Skipped on purpose:** refresh tokens, product management endpoints, payment, login rate limiting and front-end automated tests.
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
- **Testing concurrency for real.** The live race test exposed that a malformed request body returned 500 instead of 400. I mapped `BadHttpRequestException` to a problem-details 400 in every environment.
- **Background job talking to the database directly.** Its logic sat in the API project and used `DbContext`. I extracted an Application layer with ports, so the job and endpoints only call use cases, and added architecture tests that enforce this.
- **Front-end state issues:**
  - Re-rendering the whole product list on every quantity change stole input focus. I now render the table and the order panel separately.
  - Fast sort or page changes could show an older response last. I now cancel stale requests with `AbortController`.

### How AI was used

- **Planning was mine:** I broke the task into steps, decided the scope and made the key technical choices. These include SQL Server, a separate `BackEnd/` and `FrontEnd/`, Clean Architecture, interfaces for every use case, Swagger and CORS, keeping coupons in the UI, and what goes into Next steps. I used Claude Code (an AI coding agent) to implement that plan, one step at a time.
- **What the AI did:** it wrote most of the code, the tests, the Docker setup and drafts of this README, based on my instructions.
- **How I reviewed it:** I went through the generated code in detail, file by file, before accepting each step. I did not take the output as is. I questioned the design and asked for changes where it did not match what I wanted:
  - **Architecture:** in review I saw that the background job called the database directly. I had it refactored to Clean Architecture, with the job calling a use case. I moved the request and response contracts into Application `Abstractions` myself.
  - **Dependencies:** I noticed that endpoints injected concrete use case classes and asked for interfaces. An architecture test now enforces this.
  - **Concurrency:** I checked how simultaneous orders for the last unit in stock are handled. I also checked whether anything had to be thread-safe and how DI lifetimes (scoped vs singleton) isolate requests. Then I confirmed the optimistic concurrency and retry behaviour.
  - **Code style:** no comments, explicit types instead of `var`, braces on every `if`/`for` body. I deliberately did not add analyzer rules that would break the build.
  - **Requirements:** I checked the AI output against the brief. For example, I confirmed that coupons are required by the backend spec before deciding to keep them in the UI.
  - **Documentation:** I cut this README down, removed sections that added nothing, and added the tests table.
- **Verification:**
  - Every change was checked by running the full test suite.
  - API behaviour was smoke-tested against the running Docker stack.
  - The UI flow was clicked through in a browser.
  - I reviewed the final diff before committing.

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

The API listens on http://localhost:5080 and the front end stays at http://localhost:8080.

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

**Swagger UI** at http://localhost:5080/swagger lists every endpoint:
1. Call `POST /auth/login`.
2. Click **Authorize** and paste the returned `accessToken`.

The order endpoints are marked with a lock and send the token automatically.

**CORS** allows only the front-end origins listed in `Cors:AllowedOrigins` in `appsettings.json`.

Example requests are in [`BackEnd/src/Shop.Api/Shop.Api.http`](BackEnd/src/Shop.Api/Shop.Api.http). They work with the VS Code REST Client, Visual Studio or Rider. The login token is captured automatically.

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
- **`Shop.Api`** is the outer shell: minimal API endpoints that only call use cases, the background job host, JWT validation, the exception-to-ProblemDetails mapping, and the composition root that wires everything together.
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

