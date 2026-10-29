# Mini Online Shop

A small part of an online shop:

- **Back end:** a .NET 10 Web API with minimal APIs, EF Core 10 and Microsoft SQL Server 2022.
- **Front end:** native Web Components with no build step.

```
BackEnd/    .NET solution (OnlineShop.slnx): Domain / Application / Infrastructure / Api + tests, Dockerfile
FrontEnd/   Static Web Components app (index.html, src/), nginx.conf for Docker
docker-compose.yml   SQL Server + API + front end
```

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
| API | http://localhost:5080 (OpenAPI document: `/openapi/v1.json`, health: `/health`) |
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
| Coupons | `WELCOME10`: 10% off<br>`SAVE20`: 20.00 off when the subtotal is at least 100.00<br>`SPRING2024`: expired, to show the validation error<br>Coupons are applied through the API (see the `.http` file); the UI does not expose them. |

## Tests

```bash
dotnet test BackEnd/OnlineShop.slnx
```

- **Domain unit tests** (`tests/Shop.Tests/Domain`): pricing, rounding, coupons, stock reservation and status rules on the domain model.
- **Use case unit tests** (`tests/Shop.Tests/Application`): use cases with in-memory fakes and no database, including retry after a concurrency conflict and the draft sweeper skipping a conflicting order.
- **Architecture tests** (`tests/Shop.Tests/Architecture`): enforce the dependency rule between the layers.
- **Integration tests** (`tests/Shop.Tests/Integration`): the real API running in `WebApplicationFactory` against a throwaway SQL Server container from Testcontainers. The first run pulls the SQL Server image, which takes a few minutes. They cover auth, paging and sorting, the order lifecycle, ownership isolation, soft delete, the background job and seed idempotency. Docker must be running.

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

Example requests are in [`BackEnd/src/Shop.Api/Shop.Api.http`](BackEnd/src/Shop.Api/Shop.Api.http). They work with the VS Code REST Client, Visual Studio or Rider. The login token is captured automatically.

### Pricing rules

- **Server-side prices only.** Clients never send prices. Every line is priced from the product in the database, and unknown fields such as `unitPrice` or `total` are ignored.
- **Rounding.** Money is `decimal(18,2)`, rounded half away from zero. Line total = unit price × quantity; subtotal = sum of line totals; total = subtotal − discount.
- **Discount limits.** A percentage coupon is between 0 and 100 percent. A fixed discount is capped at the subtotal, so the total is never negative.
- **Coupon checks.** The coupon must be active and inside its validity window, and the subtotal must meet its minimum. These checks run again on every change. For example, removing items that drops the subtotal below `SAVE20`'s minimum is rejected unless the same request also removes the coupon.
- **Quantity limits.** Quantity is 1–100 per line, with at most 50 lines and one line per product. An order must have at least one line.
- **Draft prices.** Draft orders are re-priced from the catalogue on every change and once more when placed. After that, the prices are snapshotted on the order lines.
- **Database checks.** Check constraints back the main rules: non-negative prices, stock and totals; `Total = Subtotal - Discount`; and `LineTotal = ROUND(UnitPrice * Quantity, 2)`.

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
- **`Shop.Application`** holds one class per use case:

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
  | `order-detail` | Edit quantities, place or delete the order |

  - **Shared state and API.** `SessionStore` holds the token in `sessionStorage` and raises change events. `ApiClient` wraps `fetch`, attaches the token and turns problem details into errors. On a 401 it clears the session.
  - **Component boundaries.** Each component uses Shadow DOM. Components talk to each other through bubbling `CustomEvent`s (`order-created`, `order-selected`, `order-deleted`), and the root turns those into navigation.

## Notes

### Trade-offs

- **Repositories per aggregate, queries for lists.** Writes go through repositories that load whole aggregates, so the domain enforces every rule. Paged lists use separate query services that project straight to response models, which keeps reads cheap. This is a light CQRS split without separate databases or a mediator.
- **No MediatR.** Use cases are plain classes injected into endpoints. A mediator would add indirection without a real benefit at this size.
- **EF-friendly entities.** Domain entities have private parameterless constructors and private setters so EF Core can load them. That is the only concession to persistence; a fully separate persistence model with mapping would be extra ceremony here.
- **Exceptions for rule violations.** Business rule failures throw typed exceptions that a single handler maps to problem details. A `Result<T>` type would be more explicit, but it is noisier in a codebase this small.
- **Stock reserved at draft time.** This prevents overselling, but abandoned carts hold stock, which is why the sweeper exists. Reserving only when the order is placed is the other common choice.
- **Automatic migrations.** Migrations run on startup to keep setup to one command. In production I would run them as a separate deployment step.
- **GUID primary keys.** Ids are UUIDv7, generated in the domain so an entity has its id before it is saved. SQL Server orders `uniqueidentifier` by its last bytes, so these ids are not sequential in the clustered index. That is fine at this scale; for a large table I would generate SQL Server–friendly sequential GUIDs or use a separate clustered key.
- **Front end against port 5080.** The front end calls the API directly on port 5080 with CORS instead of going through an nginx proxy. The same `config.js` then works both in Docker and with `dotnet run`.
- **Plain template strings in components.** Components render with template strings and escape every value, rather than using a template library. That keeps the front end at zero dependencies.

### Skipped

- Refresh tokens, logout on the server, registration and roles.
- Product create, update and delete endpoints.
- Payment and a richer order lifecycle (shipping, refunds).
- Front-end automated tests.
- Rate limiting on `/auth/login`.

### Next steps with more time

- **Front end:** component tests (for example Web Test Runner with Playwright) and an end-to-end smoke test of the main flow.
- **Concurrency:** `ETag`/`If-Match` on `PATCH /orders/{id}` so clients find out when they are editing a stale order.
- **Order history:** an outbox and domain events for status changes, such as an order confirmation email when an order is placed.
- **Observability:** OpenTelemetry tracing and metrics, plus a metric for each sweeper run instead of only logs.
- **Hardening:** secrets from a vault instead of configuration, HTTPS, login rate limiting and lockout, and a CI pipeline that runs the tests and builds the image.
