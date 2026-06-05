using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Pandas.PrintAgent.Core.Security;

public sealed class SecureTokenStore : ITokenStore
{
    private const string TargetName = "Pandas.PrintAgent.AgentToken";
    private const string AccountName = "Pandas.PrintAgent";
    private const string LinuxLabel = "PANDAS Print Agent Token";

    public async Task<TokenStoreAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            return new TokenStoreAvailability(true, "Windows Credential Manager disponible.");
        }
        if (OperatingSystem.IsMacOS())
        {
            return File.Exists("/usr/bin/security")
                ? new TokenStoreAvailability(true, "macOS Keychain disponible.")
                : new TokenStoreAvailability(false, "No se encontro /usr/bin/security para usar macOS Keychain.");
        }
        if (OperatingSystem.IsLinux())
        {
            var secretTool = await FindExecutableAsync("secret-tool", cancellationToken);
            return secretTool is not null
                ? new TokenStoreAvailability(true, "Linux Secret Service disponible via secret-tool.")
                : new TokenStoreAvailability(false, "No se encontro secret-tool. Instala libsecret-tools para guardar el token de forma segura.");
        }

        return new TokenStoreAvailability(false, "Sistema operativo no soportado para almacenamiento seguro del token.");
    }

    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        await RequireAvailableAsync(cancellationToken);

        if (OperatingSystem.IsWindows())
        {
            return WindowsCredentialStore.Read(TargetName);
        }
        if (OperatingSystem.IsMacOS())
        {
            var result = await RunProcessAsync("/usr/bin/security", $"find-generic-password -s \"{TargetName}\" -a \"{AccountName}\" -w", null, cancellationToken);
            return result.ExitCode == 0 ? EmptyToNull(result.Output.Trim()) : null;
        }
        if (OperatingSystem.IsLinux())
        {
            var result = await RunProcessAsync("secret-tool", $"lookup service \"{TargetName}\" account \"{AccountName}\"", null, cancellationToken);
            return result.ExitCode == 0 ? EmptyToNull(result.Output.Trim()) : null;
        }

        throw new TokenStoreUnavailableException("Sistema operativo no soportado para almacenamiento seguro del token.");
    }

    public async Task SaveTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("El token no puede estar vacio.");
        }

        await RequireAvailableAsync(cancellationToken);

        if (OperatingSystem.IsWindows())
        {
            WindowsCredentialStore.Write(TargetName, AccountName, token);
            return;
        }
        if (OperatingSystem.IsMacOS())
        {
            await RunProcessAsync("/usr/bin/security", $"delete-generic-password -s \"{TargetName}\" -a \"{AccountName}\"", null, cancellationToken);
            var result = await RunProcessAsync("/usr/bin/security", $"add-generic-password -U -s \"{TargetName}\" -a \"{AccountName}\" -w \"{EscapeArgument(token)}\"", null, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new TokenStoreUnavailableException($"No se pudo guardar el token en macOS Keychain: {result.Error.Trim()}");
            }
            return;
        }
        if (OperatingSystem.IsLinux())
        {
            var result = await RunProcessAsync("secret-tool", $"store --label \"{LinuxLabel}\" service \"{TargetName}\" account \"{AccountName}\"", token + Environment.NewLine, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new TokenStoreUnavailableException($"No se pudo guardar el token en Linux Secret Service: {result.Error.Trim()}");
            }
            return;
        }

        throw new TokenStoreUnavailableException("Sistema operativo no soportado para almacenamiento seguro del token.");
    }

    private static async Task<string?> FindExecutableAsync(string executable, CancellationToken cancellationToken)
    {
        var command = OperatingSystem.IsWindows() ? "where" : "which";
        var result = await RunProcessAsync(command, executable, null, cancellationToken);
        return result.ExitCode == 0 ? result.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() : null;
    }

    private async Task RequireAvailableAsync(CancellationToken cancellationToken)
    {
        var availability = await CheckAvailabilityAsync(cancellationToken);
        if (!availability.IsAvailable)
        {
            throw new TokenStoreUnavailableException(availability.Message);
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, string arguments, string? standardInput, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
            process.StandardInput.Close();
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
    }

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string EscapeArgument(string value)
    {
        return value.Replace("\"", "\\\"");
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);

    private static class WindowsCredentialStore
    {
        private const int CredTypeGeneric = 1;
        private const int CredPersistLocalMachine = 2;

        public static string? Read(string targetName)
        {
            if (!CredRead(targetName, CredTypeGeneric, 0, out var credentialPointer))
            {
                return null;
            }

            try
            {
                var credential = Marshal.PtrToStructure<Credential>(credentialPointer);
                if (credential.CredentialBlobSize <= 0)
                {
                    return null;
                }

                var blob = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
                return Encoding.Unicode.GetString(blob).TrimEnd('\0');
            }
            finally
            {
                CredFree(credentialPointer);
            }
        }

        public static void Write(string targetName, string userName, string secret)
        {
            var secretBytes = Encoding.Unicode.GetBytes(secret);
            var blobPointer = Marshal.AllocCoTaskMem(secretBytes.Length);
            try
            {
                Marshal.Copy(secretBytes, 0, blobPointer, secretBytes.Length);
                var credential = new Credential
                {
                    Type = CredTypeGeneric,
                    TargetName = targetName,
                    CredentialBlob = blobPointer,
                    CredentialBlobSize = secretBytes.Length,
                    Persist = CredPersistLocalMachine,
                    UserName = userName,
                };

                if (!CredWrite(ref credential, 0))
                {
                    throw new TokenStoreUnavailableException($"No se pudo guardar el token en Windows Credential Manager. Win32Error={Marshal.GetLastWin32Error()}");
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(blobPointer);
            }
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPointer);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref Credential userCredential, int flags);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern void CredFree(IntPtr credentialPointer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string? Comment;
            public long LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string? TargetAlias;
            public string UserName;
        }
    }
}
