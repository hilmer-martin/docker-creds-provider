using Moq;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Valleysoft.DockerCredsProvider.Test;

public class CredsProviderTests
{
    [Fact]
    public async Task NativeStore()
    {
        string dockerConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".docker",
            "config.json");

        string credsStore = "desktop";

        string dockerConfigContent =
            "{" +
            $"\"credsStore\": \"{credsStore}\"" +
            "}";

        Mock<IFileSystem> fileSystemMock = new();
        fileSystemMock
            .Setup(o => o.FileExists(dockerConfigPath))
            .Returns(true);

        fileSystemMock
            .Setup(o => o.FileOpenRead(dockerConfigPath))
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes(dockerConfigContent)));

        Mock<IProcessService> processServiceMock = new();
        processServiceMock
            .Setup(o => o.Run(
                It.Is<ProcessStartInfo>(startInfo => startInfo.FileName == $"docker-credential-{credsStore}"),
                "test",
                It.IsAny<Action<string?>>(),
                It.IsAny<Action<string?>>()))
            .Callback((ProcessStartInfo startInfo, string? input, Action<string?> outputDataReceived, Action<string?> errorDataReceived) =>
            {
                outputDataReceived("{ \"Username\": \"testuser\", \"Secret\": \"password\" }");
            })
            .Returns(0);

        DockerCredentials creds = await CredsProvider.GetCredentialsAsync("test", fileSystemMock.Object, processServiceMock.Object);

        Assert.Equal("testuser", creds.Username);
        Assert.Equal("password", creds.Password);
        Assert.Null(creds.IdentityToken);
    }

    [Fact]
    public async Task NativeStore_Token()
    {
        string dockerConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".docker",
            "config.json");

        string credsStore = "desktop";

        string dockerConfigContent =
            "{" +
            $"\"credsStore\": \"{credsStore}\"" +
            "}";

        Mock<IFileSystem> fileSystemMock = new();
        fileSystemMock
            .Setup(o => o.FileExists(dockerConfigPath))
            .Returns(true);

        fileSystemMock
            .Setup(o => o.FileOpenRead(dockerConfigPath))
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes(dockerConfigContent)));

        Mock<IProcessService> processServiceMock = new();
        processServiceMock
            .Setup(o => o.Run(
                It.Is<ProcessStartInfo>(startInfo => startInfo.FileName == $"docker-credential-{credsStore}"),
                "test",
                It.IsAny<Action<string?>>(),
                It.IsAny<Action<string?>>()))
            .Callback((ProcessStartInfo startInfo, string? input, Action<string?> outputDataReceived, Action<string?> errorDataReceived) =>
            {
                outputDataReceived("{ \"Username\": \"<token>\", \"Secret\": \"identitytoken\" }");
            })
            .Returns(0);

        DockerCredentials creds = await CredsProvider.GetCredentialsAsync("test", fileSystemMock.Object, processServiceMock.Object);

        Assert.Equal("<token>", creds.Username);
        Assert.Null(creds.Password);
        Assert.Equal("identitytoken", creds.IdentityToken);
    }

    [Fact]
    public async Task NativeStore_ExeNotFound()
    {
        string dockerConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".docker",
            "config.json");

        string credsStore = "desktop";

        string dockerConfigContent =
            "{" +
            $"\"credsStore\": \"{credsStore}\"" +
            "}";

        Mock<IFileSystem> fileSystemMock = new();
        fileSystemMock
            .Setup(o => o.FileExists(dockerConfigPath))
            .Returns(true);

        fileSystemMock
            .Setup(o => o.FileOpenRead(dockerConfigPath))
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes(dockerConfigContent)));

        Mock<IProcessService> processServiceMock = new();
        processServiceMock
            .Setup(o => o.Run(
                It.Is<ProcessStartInfo>(startInfo => startInfo.FileName == $"docker-credential-{credsStore}"),
                "test",
                It.IsAny<Action<string?>>(),
                It.IsAny<Action<string?>>()))
            .Throws(new Win32Exception(2));

        await Assert.ThrowsAsync<InvalidOperationException>(() => CredsProvider.GetCredentialsAsync("test", fileSystemMock.Object, processServiceMock.Object));
    }

    [Fact]
    public async Task NativeStore_Error()
    {
        string dockerConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".docker",
            "config.json");

        string credsStore = "desktop";

        string dockerConfigContent =
            "{" +
                $"\"credsStore\": \"{credsStore}\"" +
            "}";

        Mock<IFileSystem> fileSystemMock = new();
        fileSystemMock
            .Setup(o => o.FileExists(dockerConfigPath))
            .Returns(true);

        fileSystemMock
            .Setup(o => o.FileOpenRead(dockerConfigPath))
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes(dockerConfigContent)));

        Mock<IProcessService> processServiceMock = new();
        processServiceMock
            .Setup(o => o.Run(
                It.Is<ProcessStartInfo>(startInfo => startInfo.FileName == $"docker-credential-{credsStore}"),
                "test",
                It.IsAny<Action<string?>>(),
                It.IsAny<Action<string?>>()))
            .Callback((ProcessStartInfo startInfo, string? input, Action<string?> outputDataReceived, Action<string?> errorDataReceived) =>
            {
                errorDataReceived("error msg");
            })
            .Returns(1);

        await Assert.ThrowsAsync<CredsNotFoundException>(
            () => CredsProvider.GetCredentialsAsync("test", fileSystemMock.Object, processServiceMock.Object));
    }

    [Fact]
    public async Task EncodedStore()
    {
        string dockerConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".docker",
            "config.json");

        string username = "testuser";
        string password = "testpass";

        string encodedCreds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

        string dockerConfigContent =
            "{" +
                "\"auths\": {" +
                    "\"testregistry\": {" +
                        $"\"auth\": \"{encodedCreds}\"" +
                    "}" +
                "}" +
            "}";

        Mock<IFileSystem> fileSystemMock = new();
        fileSystemMock
            .Setup(o => o.FileExists(dockerConfigPath))
            .Returns(true);

        fileSystemMock
            .Setup(o => o.FileOpenRead(dockerConfigPath))
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes(dockerConfigContent)));

        DockerCredentials creds = await CredsProvider.GetCredentialsAsync("testregistry", fileSystemMock.Object, Mock.Of<IProcessService>());

        Assert.Equal(username, creds.Username);
        Assert.Equal(password, creds.Password);
        Assert.Null(creds.IdentityToken);
    }

    [Fact]
    public async Task EncodedStore_NoMatch()
    {
        string dockerConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".docker",
            "config.json");

        string username = "testuser";
        string password = "testpass";

        string encodedCreds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

        string dockerConfigContent =
            "{" +
                "\"auths\": {" +
                    "\"testregistry\": {" +
                        $"\"auth\": \"{encodedCreds}\"" +
                    "}" +
                "}" +
            "}";

        Mock<IFileSystem> fileSystemMock = new();
        fileSystemMock
            .Setup(o => o.FileExists(dockerConfigPath))
            .Returns(true);

        fileSystemMock
            .Setup(o => o.FileOpenRead(dockerConfigPath))
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes(dockerConfigContent)));

        await Assert.ThrowsAsync<CredsNotFoundException>(
            () => CredsProvider.GetCredentialsAsync("testregistry2", fileSystemMock.Object, Mock.Of<IProcessService>()));
    }

    [Fact]
    public async Task EncodedStore_NoUsername()
    {
        string dockerConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".docker",
            "config.json");

        string username = String.Empty;
        string password = "testpass";

        string encodedCreds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

        string dockerConfigContent =
            "{" +
                "\"auths\": {" +
                    "\"testregistry\": {" +
                        $"\"auth\": \"{encodedCreds}\"" +
                    "}" +
                "}" +
            "}";

        Mock<IFileSystem> fileSystemMock = new();
        fileSystemMock
            .Setup(o => o.FileExists(dockerConfigPath))
            .Returns(true);

        fileSystemMock
            .Setup(o => o.FileOpenRead(dockerConfigPath))
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes(dockerConfigContent)));

        await Assert.ThrowsAsync<JsonException>(() =>
            CredsProvider.GetCredentialsAsync("testregistry", fileSystemMock.Object, Mock.Of<IProcessService>())
        );
    }

    [Fact]
    public async Task EncodedStore_NoPassword()
    {
        string dockerConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".docker",
            "config.json");

        string username = "testuser";
        string password = "";

        string encodedCreds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

        string dockerConfigContent =
            "{" +
                "\"auths\": {" +
                    "\"testregistry\": {" +
                        $"\"auth\": \"{encodedCreds}\"" +
                    "}" +
                "}" +
            "}";

        Mock<IFileSystem> fileSystemMock = new();
        fileSystemMock
            .Setup(o => o.FileExists(dockerConfigPath))
            .Returns(true);

        fileSystemMock
            .Setup(o => o.FileOpenRead(dockerConfigPath))
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes(dockerConfigContent)));

        await Assert.ThrowsAsync<JsonException>(() =>
            CredsProvider.GetCredentialsAsync("testregistry", fileSystemMock.Object, Mock.Of<IProcessService>())
        );
    }

    [Fact]
    public async Task EncodedStore_NoSeparator()
    {
        string dockerConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".docker",
            "config.json");

        string username = "testuser";
        string password = "testpassword";

        string encodedCreds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}{password}"));

        string dockerConfigContent =
            "{" +
                "\"auths\": {" +
                    "\"testregistry\": {" +
                        $"\"auth\": \"{encodedCreds}\"" +
                    "}" +
                "}" +
            "}";

        Mock<IFileSystem> fileSystemMock = new();
        fileSystemMock
            .Setup(o => o.FileExists(dockerConfigPath))
            .Returns(true);

        fileSystemMock
            .Setup(o => o.FileOpenRead(dockerConfigPath))
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes(dockerConfigContent)));

        await Assert.ThrowsAsync<JsonException>(() =>
            CredsProvider.GetCredentialsAsync("testregistry", fileSystemMock.Object, Mock.Of<IProcessService>())
        );
    }

    [Fact]
    public async Task EncodedStore_IdentityTokenWithUsernamePasswordSeparator()
    {
        string dockerConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".docker",
            "config.json");

        string username = "00000000-0000-0000-0000-000000000000";
        string password = String.Empty;
        string token = "tokenstring";

        string encodedCreds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

        string dockerConfigContent =
            "{" +
                "\"auths\": {" +
                    "\"testregistry\": {" +
                        $"\"auth\": \"{encodedCreds}\"," +
                        $"\"identitytoken\": \"{token}\"" +
                    "}" +
                "}" +
            "}";

        Mock<IFileSystem> fileSystemMock = new();
        fileSystemMock
            .Setup(o => o.FileExists(dockerConfigPath))
            .Returns(true);

        fileSystemMock
            .Setup(o => o.FileOpenRead(dockerConfigPath))
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes(dockerConfigContent)));

        DockerCredentials creds = await CredsProvider.GetCredentialsAsync("testregistry", fileSystemMock.Object, Mock.Of<IProcessService>());
        Assert.Equal(username, creds.Username);
        Assert.Null(creds.Password);
        Assert.Equal(token, creds.IdentityToken);
    }

    [Fact]
    public async Task EncodedStore_IdentityTokenWithoutUsernamePasswordSeparator()
    {
        string dockerConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".docker",
            "config.json");

        string username = "<token>";
        string token = "tokenstring";

        string encodedCreds = Convert.ToBase64String(Encoding.UTF8.GetBytes(username));

        string dockerConfigContent =
            "{" +
                "\"auths\": {" +
                    "\"testregistry\": {" +
                        $"\"auth\": \"{encodedCreds}\"," +
                        $"\"identitytoken\": \"{token}\"" +
                    "}" +
                "}" +
            "}";

        Mock<IFileSystem> fileSystemMock = new();
        fileSystemMock
            .Setup(o => o.FileExists(dockerConfigPath))
            .Returns(true);

        fileSystemMock
            .Setup(o => o.FileOpenRead(dockerConfigPath))
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes(dockerConfigContent)));

        DockerCredentials creds = await CredsProvider.GetCredentialsAsync("testregistry", fileSystemMock.Object, Mock.Of<IProcessService>());
        Assert.Equal(username, creds.Username);
        Assert.Null(creds.Password);
        Assert.Equal(token, creds.IdentityToken);
    }

    [Fact]
    public Task NullRegistry()
    {
        return Assert.ThrowsAsync<ArgumentNullException>(() => CredsProvider.GetCredentialsAsync(null!));
    }

    [Fact]
    public async Task ConfigFileDoesNotExist()
    {
        string dockerConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".docker",
            "config.json");

        Mock<IFileSystem> fileSystemMock = new();
        fileSystemMock
            .Setup(o => o.FileExists(dockerConfigPath))
            .Returns(false);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => CredsProvider.GetCredentialsAsync("test", fileSystemMock.Object, Mock.Of<IProcessService>()));
    }
}
