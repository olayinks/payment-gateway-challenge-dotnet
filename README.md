# Payment Gateway Challenge — .NET

A simple ASP.NET Core payment gateway built for the checkout.com challenge.

## Project overview

- `src/PaymentGateway.Api` — the Web API
- `test/PaymentGateway.Api.Tests` — unit and integration tests
- `test/PaymentGateway.Api.ContractTests` — consumer contract tests for the bank API boundary
- `imposters/` — bank simulator configuration (Mountebank)
- `docker-compose.yml` — starts the bank simulator locally

## API Endpoints

### `POST /api/v1/payments`

Submits a new payment for authorization.

Headers:
- `Idempotency-Key` (optional): same key + same payload returns the original result without calling the bank again.

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
- `201 Created` — payment authorized. The `Location` header points to `GET /api/v1/payments/{id}`.
- `200 OK` — idempotent replay of an authorized payment.
- `402 Payment Required` — payment declined by the bank, or the bank was unreachable.
- `400 Bad Request` — validation failed.
- `409 Conflict` — idempotency key reused with a different payload.

### `GET /api/v1/payments/{id}`

Fetches a stored payment by ID.

Responses:
- `200 OK` — payment found.
- `404 Not Found` — no payment with that ID.

### `GET /health`

Basic liveness check.

## Key behaviors

- Validation runs via FluentValidation before the bank is called.
- Supported currencies: `EUR`, `GBP`, `USD`.
- Idempotency is keyed on a trimmed `Idempotency-Key` header.
- Declined payments are stored and returned with `402 Payment Required`.
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

3. The API connects to the bank at `http://localhost:8080` by default. Swagger is available at `/swagger` in development mode.

## Configuration

Bank settings live under `BankApi` in `appsettings.json`:

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

The contract tests generate a pact file at `test/PaymentGateway.Api.ContractTests/bin/Debug/net8.0/pacts/Payment Gateway-Bank API.json`.

## Notes and trade-offs

I kept the solution focused on the challenge scope rather than building a production system. Here's where I drew the line and why.

### Persistence

Payments and idempotency records are stored in memory. Data survives across requests within a single run but is lost on restart, and the repository isn't thread-safe under concurrent writes. In production I'd swap this for a durable store with proper transaction support.

`IPaymentsRepository` is registered as a singleton — that's deliberate. It's what makes the in-memory list act as a shared store across all requests. 

### Idempotency

The idempotency key is trimmed and stored alongside a SHA-256 hash of the request. Same key + same payload returns the original result; same key + different payload returns `409 Conflict`.

This works fine for sequential requests but has a race condition under concurrency — two simultaneous requests with the same new key could both pass the check and hit the bank. In production the check-and-record would need to be atomic, ideally enforced at the database level.

### Bank failures

Bank declines (`authorized: false`) and transport failures (e.g. `503 Service Unavailable`) both result in a `Declined` payment. For this challenge that felt reasonable, but in a real production system I'd treat a transport failure differently — it's an unknown outcome, not a confirmed decline. Conflating the two could mean telling a customer their payment failed when the bank actually charged them.

### Async processing

The API calls the bank synchronously, so the caller gets a result immediately. That's the simplest approach and easy to reason about.

In production I'd consider publishing to a message broker instead — save the payment, emit an event, return `Pending`, and let a worker handle the bank call asynchronously. That makes the API more resilient to bank latency and outages, but adds real complexity: you'd need an outbox pattern to avoid losing events, an inbox pattern on the worker side to handle duplicates, and the API would need to deal with eventual consistency.

### Validation

Card numbers are validated for digit length and by FluentValidation's built-in `CreditCard` rule, which runs a Luhn check. CVV accepts 3 or 4 digits.

### API Design

The API is versioned under `/api/v1/`. Breaking changes would get a new version prefix rather than modifying the existing contract.

Authorized payments return `201 Created` with a `Location` header. Declined payments return `402 Payment Required` — the HTTP status code is used for this case scenarios. It lets callers detect a failed payment from the status code alone without parsing the body, and it keeps the error path consistent with how most HTTP clients handle failures. An idempotent replay of an authorized payment returns `200 OK`; a replay of a declined payment returns `402` again, since the outcome hasn't changed. In production system, I would implement an asynchronous architecture with a a message broker and a retry mechanism to manage such scenarios.

The API has no authentication — any caller can submit or retrieve payments. In production I would need at minimum an API key per merchant. Preferrably a Token based authentication and authorization (OAuth2 + OpenID Connect) security would be implemented. There's also no rate limiting, which matters for a payment endpoint. Both are intentionally out of scope here.

### Observability

Structured logging covers all meaningful events: validation failures, idempotency replays, bank call start and outcome, and bank errors. Each entry uses named properties (`{PaymentId}`, `{CardLastFour}`, `{Amount}`, `{Currency}`) so you can filter by them in any structured log sink.

The payment ID is assigned before the bank call, so it appears in every log entry for that payment — including the bank call itself. The bank's own reference (`authorization_code`) is separate; the gateway ID is ours and exists independently of what the bank returns.

There's no client-supplied correlation ID (per design requirement), so a caller can't tie their own request ID to our logs. The `/health` endpoint is liveness-only and always returns 200 — there are no registered checks behind it. Metrics and distributed tracing are out of scope; in production you'd want payment volume, bank latency percentiles, and decline rate at minimum.

### Hosting

The API runs with `dotnet run` and the bank simulator via `docker compose`. A production setup would add a Dockerfile, a CI pipeline, and separate liveness/readiness probes.

### Testing

- **Unit tests** cover validation, the payment service, idempotency logic, and controller responses. `PaymentServiceTests` mocks both the validator and bank client — the in-memory repository is used directly since it has no I/O and is deterministic.
- **Integration tests** cover the full API pipeline with a mocked `IBankClient`.
- **Contract tests** use [PactNet](https://github.com/pact-foundation/pact-net) to verify the HTTP boundary with the bank. They run `BankClient` directly against a PactNet mock server — if a field name or type drifts, the test fails. Three scenarios are covered: bank approves, bank declines, and bank returns `400`. No Docker required.

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
