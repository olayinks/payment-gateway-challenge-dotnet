# Payment Gateway Challenge — .NET

A simple ASP.NET Core payment gateway built for the checkout.com challenge.

## Project overview

- `src/PaymentGateway.Api` — the Web API
- `test/PaymentGateway.Api.Tests` — unit and integration tests
- `test/PaymentGateway.Api.ContractTests` — consumer contract tests for the bank API boundary
- `imposters/` — bank simulator configuration (Mountebank)
- `docker-compose.yml` — starts the bank simulator locally

## API Endpoints

### `POST /api/payments`

Submits a new payment for authorization.

Headers:
- `Idempotency-Key` (optional): if provided, the same key with the same payload returns the original result instead of calling the bank again.

Request body:
```json
{
  "cardNumber": "4111111111111111",
  "expiryMonth": 12,
  "expiryYear": 2028,
  "currency": "GBP",
  "amount": 1250,
  "cvv": "123"
}
```

Responses:
- `201 Created` — new payment authorized and stored. The `Location` header points to `GET /api/payments/{id}`.
- `200 OK` — payment declined, or an existing idempotent result is returned.
- `400 Bad Request` — validation failed.
- `409 Conflict` — idempotency key reused with a different payload.

### `GET /api/payments/{id}`

Fetches a stored payment by ID.

Responses:
- `200 OK` — payment found.
- `404 Not Found` — no payment with that ID.

### `GET /health`

Basic liveness check.

## Key behaviors

- Validation runs via FluentValidation before the bank is called.
- Supported currencies: `EUR`, `GBP`, `USD`.
- Idempotency uses an in-memory store keyed on a trimmed `Idempotency-Key` header.
- Declined payments are stored and returned with `200 OK`, not an error.
- New authorized payments return `201 Created` with a `Location` header.

## Running locally

1. Start the bank simulator:
   ```powershell
   docker compose up -d bank_simulator
   ```

2. Start the API:
   ```powershell
   dotnet run --project src/PaymentGateway.Api/PaymentGateway.Api.csproj
   ```

3. The API connects to the bank at `http://localhost:8080` by default. Swagger is available in development mode.

## Configuration

Bank settings live under `BankApi` in `appsettings.json` / `appsettings.Development.json`:

```json
"BankApi": {
  "BaseUrl": "http://localhost:8080",
  "PaymentEndpoint": "/payments",
  "TimeoutSeconds": 30
}
```

## Testing

Run everything:
```powershell
dotnet test
```

Or target a specific project:
- `dotnet test test/PaymentGateway.Api.Tests/PaymentGateway.Api.Tests.csproj`
- `dotnet test test/PaymentGateway.Api.Integration.Tests/PaymentGateway.Api.Integration.Tests.csproj`
- `dotnet test test/PaymentGateway.Api.ContractTests/PaymentGateway.Api.ContractTests.csproj`

Running the contract tests generates a pact file at `test/PaymentGateway.Api.ContractTests/bin/Debug/net8.0/pacts/Payment Gateway-Bank API.json`.

## Notes and trade-offs

I kept the solution focused on the challenge scope rather than building a production system. Here's where I drew the line and why.

### Persistence

Payments and idempotency records are stored in memory in a singleton repository. This means data survives across requests within a single process run, but is lost on restart and is not thread-safe under concurrent writes. In production I'd swap this for a durable store with proper transaction support.

### Idempotency

The idempotency key is trimmed and stored alongside a SHA-256 hash of the request fields. Same key + same payload returns the original payment; same key + different payload returns `409 Conflict`.

The current implementation works fine for sequential requests but has a race condition under concurrency — two simultaneous requests with the same new key could both pass the check and hit the bank. In production the check-and-record would need to be a single atomic operation, ideally enforced at the database level.

### Bank failures

I map bank declines (`authorized: false`) and transport failures (e.g. `503 Service Unavailable`) both to a persisted `Declined` payment. For this challenge that felt reasonable, but in a real payment system I'd treat a transport failure differently — it's an unknown outcome, not a confirmed decline, and conflating the two could mean incorrectly telling a customer their payment failed when the bank actually charged them.

### Async processing

The API calls the bank synchronously during `POST /api/payments`, so the caller gets a result immediately. That's simple and easy to reason about here.

If I were building this for production I'd consider publishing to a message broker instead — save the payment, emit an authorization event, and return a `Pending` status while a worker calls the bank asynchronously. That would make the API more resilient to bank latency and short outages. The trade-off is real complexity: you'd need an outbox pattern to avoid losing messages after a save, an inbox pattern on the worker side to handle duplicates safely, and the API would need to deal with eventual consistency.

### Validation

Card numbers are validated for digit length and by FluentValidation's built-in `CreditCard` rule, which runs a Luhn check. This is stricter than a plain length check — I treated that as a reasonable assumption for a gateway. CVV accepts 3 or 4 digits.

### Observability

Structured logging covers validation failures, idempotency replays, bank calls, and bank errors. There's no client-supplied correlation ID, so tracing a single request through logs relies on the framework's request context. Metrics, distributed tracing, and richer health checks are intentionally out of scope for this exercise.

### Hosting

The API runs with `dotnet run` and the bank simulator via `docker compose`. A production setup would add a Dockerfile for the API, a CI pipeline, environment-specific config validation, and separate liveness/readiness probes.

### Testing

- **Unit tests** cover validation, the payment service, idempotency logic, and controller responses.
- **Integration tests** cover the full API pipeline with a mocked `IBankClient`.
- **Contract tests** use [PactNet](https://github.com/pact-foundation/pact-net) to verify the HTTP boundary with the bank API. They exercise `BankClient` directly against a real PactNet mock server that enforces the request shape — if a JSON field name or type drifts, the test fails. Three scenarios are covered: bank approves, bank declines, and bank returns `400`. These run in-process without Docker.

- The bank approves the payment — `authorized: true` with a non-empty `authorization_code`.
- The bank declines the payment — `authorized: false` with a null `authorization_code`.
- The bank returns `400 Bad Request` — `BankClient` propagates an `HttpRequestException`.

## Structure

```
src/
  PaymentGateway.Api/
    Controllers/
    Services/
    Repository/
    Models/
    Mapper/
    Configuration/

test/
  PaymentGateway.Api.Tests/
  PaymentGateway.Api.Integration.Tests/
  PaymentGateway.Api.ContractTests/
```