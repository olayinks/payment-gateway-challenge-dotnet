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
- `201 Created` when a new payment is accepted and stored.
- `200 OK` when the payment is declined or an existing idempotent payment is returned.
- `400 Bad Request` when validation fails.
- `409 Conflict` when the idempotency key is reused with a different payload.

### `GET /api/payments/{id}`

Retrieves payment details by payment ID.

Responses:
- `200 OK` with payment details when the payment exists.
- `404 Not Found` when the payment cannot be found.

## Key behaviors

- Validation is performed using FluentValidation.
- Idempotency is supported via an in-memory idempotency store.
- Bank authorization is performed through an `IBankClient` implementation using `HttpClient`.
- The payment store is currently in-memory and scoped as a singleton for the lifetime of the application.
- Declined payments are persisted and returned with a `200 OK` response.

## Running locally

1. Start the bank simulator and app together:
   ```powershell
   docker-compose up --build
   ```

2. Start the API directly:
   ```powershell
   dotnet run --project src/PaymentGateway.Api/PaymentGateway.Api.csproj
   ```

3. The API will be available at the configured local URL, and Swagger is enabled in development mode.

## Configuration

The API reads bank settings from `appsettings.json` / `appsettings.Development.json` under the `BankApi` section.

Example configuration values:
```json
"BankApi": {
  "BaseUrl": "http://localhost:9000",
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

- The current implementation uses an in-memory repository. This is not Thread safe, but left to be simple. This is acceptable for the coding challenge 
- Idempotency matching is based on payload hash plus trimmed idempotency key.
- The service intentionally accepts declined payments and exposes them through the API rather than returning an HTTP error.
- Configuration validation is minimal; missing or invalid bank settings may fail at runtime.

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
