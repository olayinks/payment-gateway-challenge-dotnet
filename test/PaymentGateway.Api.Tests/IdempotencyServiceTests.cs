using Microsoft.Extensions.Logging;

using PaymentGateway.Api.Exceptions;
using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models.Domain;
using PaymentGateway.Api.Repository;
using PaymentGateway.Api.Services;

using Shouldly;

namespace PaymentGateway.Api.Tests;

public class IdempotencyServiceTests
{
    [Fact]
    public void Check_returns_null_when_key_is_null()
    {
        var service = CreateService(new PaymentsRepository());
        service.Check(null, TestData.PaymentRequest()).ShouldBeNull();
    }

    [Fact]
    public void Check_returns_null_when_key_is_whitespace()
    {
        var service = CreateService(new PaymentsRepository());
        service.Check("   ", TestData.PaymentRequest()).ShouldBeNull();
    }

    [Fact]
    public void Check_returns_null_when_no_existing_record()
    {
        var service = CreateService(new PaymentsRepository());
        service.Check("new-key", TestData.PaymentRequest()).ShouldBeNull();
    }

    [Fact]
    public void Check_returns_existing_payment_when_record_matches()
    {
        var repository = new PaymentsRepository();
        var service = CreateService(repository);
        var request = TestData.PaymentRequest();
        var payment = new Payment { Id = Guid.NewGuid() };
        repository.Add(payment);
        service.Record("test-key", request, payment.Id);

        var result = service.Check("test-key", request);

        result.ShouldNotBeNull();
        result.AlreadyProcessed.ShouldBeTrue();
        result.Payment!.Id.ShouldBe(payment.Id);
    }

    [Fact]
    public void Check_throws_conflict_when_different_payload_uses_same_key()
    {
        var repository = new PaymentsRepository();
        var service = CreateService(repository);
        var payment = new Payment { Id = Guid.NewGuid() };
        repository.Add(payment);
        service.Record("test-key", TestData.PaymentRequest("4111111111111111"), payment.Id);

        Should.Throw<IdempotencyConflictException>(() =>
            service.Check("test-key", TestData.PaymentRequest("4242424242424242")));
    }

    [Fact]
    public void Check_trims_key_before_lookup()
    {
        var repository = new PaymentsRepository();
        var service = CreateService(repository);
        var request = TestData.PaymentRequest();
        var payment = new Payment { Id = Guid.NewGuid() };
        repository.Add(payment);
        service.Record("test-key", request, payment.Id);

        var result = service.Check("  test-key  ", request);

        result.ShouldNotBeNull();
        result.AlreadyProcessed.ShouldBeTrue();
    }

    [Fact]
    public void Record_stores_trimmed_key()
    {
        var repository = new PaymentsRepository();
        var service = CreateService(repository);
        var payment = new Payment { Id = Guid.NewGuid() };
        repository.Add(payment);

        service.Record("  test-key  ", TestData.PaymentRequest(), payment.Id);

        repository.GetIdempotencyRecord("test-key").ShouldNotBeNull();
    }

    private static IdempotencyService CreateService(IPaymentsRepository repository) =>
        new(repository, new Logger<IdempotencyService>(new LoggerFactory()));
}
