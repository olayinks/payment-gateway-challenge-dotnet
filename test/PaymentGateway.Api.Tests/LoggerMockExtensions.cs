using Microsoft.Extensions.Logging;

using Moq;

namespace PaymentGateway.Api.Tests;

public static class LoggerMockExtensions
{
    public static void VerifyLog<T>(
        this Mock<ILogger<T>> loggerMock,
        LogLevel logLevel,
        string expectedMessage,
        Times times)
    {
        loggerMock.Verify(
            logger => logger.Log(
                logLevel,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString() != null &&
                    state.ToString()!.Contains(expectedMessage)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }

    public static void VerifyLogDoesNotContain<T>(
        this Mock<ILogger<T>> loggerMock,
        string unexpectedMessage)
    {
        loggerMock.Verify(
            logger => logger.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString() != null &&
                    state.ToString()!.Contains(unexpectedMessage)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
