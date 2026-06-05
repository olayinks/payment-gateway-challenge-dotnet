# Payment Gateway Challenge — .NET

This repository contains a sample ASP.NET Core Web API implementation for a simple payment gateway challenge.

## Project overview

- `src/PaymentGateway.Api` contains the Web API.
- `test/PaymentGateway.Api.Tests` contains unit and integration tests.
- `imposters/` contains the bank simulator configuration used by the challenge.
- `docker-compose.yml` configures the bank simulator and local test environment.

This solution is intentionally simple and focused on the required challenge behavior rather than a full production-ready system.

## API Endpoints

### `POST /api/payments`

Creates a payment request.

Headers:
- `Idempotency-Key` (optional): if provided, the request is treated idempotently.

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
- `201 Created` when a new payment is accepted and stored. The `Location` header is generated from the `GET /api/payments/{id}` route.
- `200 OK` when the payment is declined or an existing idempotent payment is returned.
- `400 Bad Request` when validation fails.
- `409 Conflict` when the idempotency key is reused with a different payload.

### `GET /api/payments/{id}`

Retrieves payment details by payment ID.

Responses:
- `200 OK` with payment details when the payment exists.
- `404 Not Found` when the payment cannot be found.

### `GET /health`

Returns the application health status.

Responses:
- `200 OK` when the API is running.

## Key behaviors

- Validation is performed using FluentValidation.
- Supported payment currencies are `EUR`, `GBP`, and `USD`.
- Idempotency is supported via an in-memory idempotency store.
- Bank authorization is performed through an `IBankClient` implementation using `HttpClient`.
- The payment store is currently in-memory and scoped as a singleton for the lifetime of the application.
- Declined payments are persisted and returned with a `200 OK` response.
- New authorized payments return `201 Created` using route-based URL generation via `CreatedAtAction`.
- A basic `/health` endpoint is exposed for hosting checks.

## Running locally

1. Start the bank simulator:
   ```powershell
   docker compose up -d bank_simulator
   ```

2. Start the API:
   ```powershell
   dotnet run --project src/PaymentGateway.Api/PaymentGateway.Api.csproj
   ```

3. The API uses the development bank URL `http://localhost:8080` by default.
   Swagger is enabled in development mode at the API's configured local URL.

## Configuration

The API reads bank settings from `appsettings.json` / `appsettings.Development.json` under the `BankApi` section.

Example configuration values:
```json
"BankApi": {
  "BaseUrl": "http://localhost:8080",
  "PaymentEndpoint": "/payments",
  "TimeoutSeconds": 30
}
```

## Testing

Run all projects:
```powershell
dotnet test
```

Specific test projects:
- `dotnet test test/PaymentGateway.Api.Tests/PaymentGateway.Api.Tests.csproj`
- `dotnet test test/PaymentGateway.Api.Integration.Tests/PaymentGateway.Api.Integration.Tests.csproj`

## Notes and assumptions

This section captures the deliberate trade-offs made to keep the solution focused on the challenge rather than a production-ready payment system.

### Persistence

- Payments and idempotency records are stored in memory.
- The repository is registered as a singleton so data survives across requests for the lifetime of the process.
- The in-memory store is intentionally not durable and not thread-safe. A production system would use a durable data store with transactional guarantees.

### Idempotency

- Idempotency is implemented using a trimmed `Idempotency-Key` header plus a SHA-256 hash of the request payload.
- Reusing a key with the same payload returns the original payment.
- Reusing a key with a different payload returns `409 Conflict`.
- The current implementation is suitable for sequential challenge scenarios, but it is not atomic under concurrent requests. In production, the check, write, and payment persistence would need to happen in a single transactional operation.

### Bank failures and payment outcome

- Explicit bank authorization responses are mapped to `Authorized` or `Declined`.
- The current implementation also maps bank transport failures, such as `503 Service Unavailable`, to a persisted `Declined` payment with an error message.
- This is a simplification for the challenge. In a production payment system, a bank outage or timeout should usually be treated as an unknown technical outcome rather than a confirmed customer-facing decline.

### Validation

- Card data is validated before calling the bank.
- Supported currencies are limited to `EUR`, `GBP`, and `USD`.
- Card numbers are validated for digit length and by FluentValidation's `CreditCard` rule. This is stricter than a basic numeric-length check and is an intentional assumption.
- CVV is accepted as 3 or 4 digits.

### Observability

- The API uses structured logging around validation, idempotency replay, bank calls, and bank errors.
- A basic `/health` endpoint is exposed.
- The API does not accept a client-supplied request ID, so request correlation currently relies on framework-generated request context rather than an explicit business/request identifier.
- Metrics, tracing, correlation IDs, and deeper readiness checks are intentionally out of scope for this exercise.

### Hosting and packaging

- `docker-compose.yml` starts the provided Mountebank bank simulator.
- The API is run directly with `dotnet run`.
- A production packaging path would add an API Dockerfile, CI pipeline, environment-specific configuration validation, and separate liveness/readiness checks.

### Testing

- Unit tests cover validation, the payment service, idempotency behavior, controller responses, and the bank client.
- Integration tests cover the API pipeline with a mocked `IBankClient`.
- The current integration tests do not exercise the real Mountebank simulator; a production-grade test strategy would add simulator-backed contract tests for the bank boundary.

## Structure

```
src/
  PaymentGateway.Api
    Controllers/
    Services/
    Repository/
    Models/
    Mapper/
    Configuration/

test/
  PaymentGateway.Api.Tests
  PaymentGateway.Api.Integration.Tests
```
