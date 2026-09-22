using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using OpenPredator.Core.Protocol;

namespace OpenPredator.Service.Ipc;

public class NamedPipeServer
{
    private const string PipeName = "predatorsense_service_namedpipe";
    private const string LinuxSocketPath = "/tmp/predatorsense_service_namedpipe";

    private readonly CommandDispatcher _dispatcher = new();
    private CancellationTokenSource? _cts;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        if (OperatingSystem.IsWindows())
        {
            _ = Task.Run(() => RunWindowsPipeLoopAsync(_cts.Token));
        }
        else
        {
            _ = Task.Run(() => RunUnixSocketLoopAsync(_cts.Token));
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    private async Task RunWindowsPipeLoopAsync(CancellationToken ct)
    {
        Console.WriteLine($"[IPC] Starting Named Pipe Server: \\\\.\\pipe\\{PipeName}");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                NamedPipeServerStream serverStream;

#pragma warning disable CA1416
                try
                {
                    // Create security descriptor granting Full Control to Everyone & Authenticated Users
                    var pipeSecurity = new PipeSecurity();
                    var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                    var authUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);

                    pipeSecurity.AddAccessRule(new PipeAccessRule(
                        everyone,
                        PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                        AccessControlType.Allow));
                    pipeSecurity.AddAccessRule(new PipeAccessRule(
                        authUsers,
                        PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                        AccessControlType.Allow));

                    serverStream = NamedPipeServerStreamAcl.Create(
                        PipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous,
                        inBufferSize: 4096,
                        outBufferSize: 4096,
                        pipeSecurity);
                }
                catch
                {
                    // Fallback to standard creation
                    serverStream = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                }
#pragma warning restore CA1416

                await serverStream.WaitForConnectionAsync(ct);
                _ = Task.Run(() => HandleClientAsync(serverStream, ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IPC] Pipe listener error: {ex.Message}");
                await Task.Delay(500, ct);
            }
        }
    }

    private async Task RunUnixSocketLoopAsync(CancellationToken ct)
    {
        Console.WriteLine($"[IPC] Starting Unix Domain Socket Server: {LinuxSocketPath}");

        if (File.Exists(LinuxSocketPath))
        {
            try { File.Delete(LinuxSocketPath); } catch { }
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        var endpoint = new UnixDomainSocketEndPoint(LinuxSocketPath);
        socket.Bind(endpoint);
        socket.Listen(10);

        try
        {
            // Give all users permission to connect
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(LinuxSocketPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite |
                    UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                    UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
            }
        }
        catch { }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var clientSocket = await socket.AcceptAsync(ct);
                var stream = new NetworkStream(clientSocket, ownsSocket: true);
                _ = Task.Run(() => HandleClientAsync(stream, ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IPC] Unix socket error: {ex.Message}");
                await Task.Delay(500, ct);
            }
        }
    }

    private async Task HandleClientAsync(Stream stream, CancellationToken ct)
    {
        using (stream)
        {
            byte[] buffer = new byte[4096];
            while (!ct.IsCancellationRequested)
            {
                int bytesRead;
                try
                {
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                    if (bytesRead <= 0) break;
                }
                catch
                {
                    break;
                }

                if (PacketSerializer.TryDeserializeRequest(buffer.AsSpan(0, bytesRead), out var command, out var args))
                {
                    byte[] response = await _dispatcher.DispatchAsync(command, args);
                    await stream.WriteAsync(response, 0, response.Length, ct);
                    await stream.FlushAsync(ct);
                }
            }
        }
    }
}
