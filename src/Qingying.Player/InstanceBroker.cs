using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace Qingying;

public sealed class InstanceBroker : IDisposable
{
    public const string MutexName = @"Local\Qingying.Player";
    private const string PipeName = "Qingying.Player.v1";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cancellation = new();
    private int _disposed;

    public InstanceBroker(string[] startupArgs)
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsPrimary = createdNew;
        if (!createdNew)
        {
            Send(startupArgs);
        }
    }

    public bool IsPrimary { get; }

    public event Action<IReadOnlyList<string>>? FilesReceived;

    public void StartListening()
    {
        if (!IsPrimary)
        {
            return;
        }

        _ = Task.Run(() => Listen(_cancellation.Token));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        if (IsPrimary)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _mutex.Dispose();
        _cancellation.Dispose();
    }

    private async Task Listen(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server);
                var payload = await reader.ReadToEndAsync(cancellationToken);
                var files = JsonSerializer.Deserialize<string[]>(payload) ?? [];
                if (files.Length > 0)
                {
                    FilesReceived?.Invoke(files);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (IOException)
            {
            }
        }
    }

    private static void Send(string[] args)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(400);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.Write(JsonSerializer.Serialize(args));
                return;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                Thread.Sleep(150);
            }
        }
    }
}
