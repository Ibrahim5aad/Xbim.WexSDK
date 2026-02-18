using Xunit;

namespace Xbim.WexServer.Client.Tests;

public class TokenProviderTests
{
    public class StaticTokenProviderTests
    {
        [Fact]
        public async Task GetTokenAsync_WithToken_ReturnsToken()
        {
            // Arrange
            var provider = new StaticTokenProvider("test-token");

            // Act
            var token = await provider.GetTokenAsync();

            // Assert
            Assert.Equal("test-token", token);
        }

        [Fact]
        public async Task GetTokenAsync_WithNull_ReturnsNull()
        {
            // Arrange
            var provider = new StaticTokenProvider(null);

            // Act
            var token = await provider.GetTokenAsync();

            // Assert
            Assert.Null(token);
        }

        [Fact]
        public async Task GetTokenAsync_CalledMultipleTimes_ReturnsSameToken()
        {
            // Arrange
            var provider = new StaticTokenProvider("constant-token");

            // Act
            var token1 = await provider.GetTokenAsync();
            var token2 = await provider.GetTokenAsync();

            // Assert
            Assert.Equal(token1, token2);
        }
    }

    public class DelegateTokenProviderTests
    {
        [Fact]
        public async Task GetTokenAsync_WithAsyncFactory_CallsFactory()
        {
            // Arrange
            var provider = new DelegateTokenProvider(async ct =>
            {
                await Task.Delay(1, ct);
                return "async-token";
            });

            // Act
            var token = await provider.GetTokenAsync();

            // Assert
            Assert.Equal("async-token", token);
        }

        [Fact]
        public async Task GetTokenAsync_WithSyncFactory_CallsFactory()
        {
            // Arrange
            var callCount = 0;
            var provider = new DelegateTokenProvider(() =>
            {
                callCount++;
                return "sync-token";
            });

            // Act
            var token = await provider.GetTokenAsync();

            // Assert
            Assert.Equal(1, callCount);
            Assert.Equal("sync-token", token);
        }

        [Fact]
        public async Task GetTokenAsync_FactoryReturnsNull_ReturnsNull()
        {
            // Arrange
            var provider = new DelegateTokenProvider(_ => Task.FromResult<string?>(null));

            // Act
            var token = await provider.GetTokenAsync();

            // Assert
            Assert.Null(token);
        }

        [Fact]
        public async Task GetTokenAsync_PassesCancellationToken()
        {
            // Arrange
            CancellationToken capturedToken = default;
            var provider = new DelegateTokenProvider(ct =>
            {
                capturedToken = ct;
                return Task.FromResult<string?>("token");
            });

            var cts = new CancellationTokenSource();

            // Act
            await provider.GetTokenAsync(cts.Token);

            // Assert
            Assert.Equal(cts.Token, capturedToken);
        }

        [Fact]
        public void Constructor_WithNullAsyncFactory_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new DelegateTokenProvider((Func<CancellationToken, Task<string?>>)null!));
        }

        [Fact]
        public void Constructor_WithNullSyncFactory_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new DelegateTokenProvider((Func<string?>)null!));
        }

        [Fact]
        public async Task GetTokenAsync_CalledMultipleTimes_CallsFactoryEachTime()
        {
            // Arrange
            var callCount = 0;
            var provider = new DelegateTokenProvider(_ =>
            {
                callCount++;
                return Task.FromResult<string?>($"token-{callCount}");
            });

            // Act
            var token1 = await provider.GetTokenAsync();
            var token2 = await provider.GetTokenAsync();

            // Assert
            Assert.Equal(2, callCount);
            Assert.Equal("token-1", token1);
            Assert.Equal("token-2", token2);
        }
    }
}
