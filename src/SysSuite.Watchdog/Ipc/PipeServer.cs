using System.IO.Pipes;
using System.Text;

namespace SysSuite.Watchdog.Ipc;

public sealed class PipeServer : IAsyncDisposable
{
    private const string PipeName = "SysSuite.Watchdog";
    private readonly CancellationTokenSource cancellation = new();

    public async Task StartAsync(Action<JsonFrame> frameReceived)
    {
        while (!cancellation.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            try
            {
                await server.WaitForConnectionAsync(cancellation.Token);
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                var request = await reader.ReadToEndAsync(cancellation.Token);
                var frame = JsonFrame.Deserialize(request) ?? new JsonFrame("Unknown", Guid.NewGuid().ToString(), null);
                frameReceived(frame);
                var response = new JsonFrame(
                    frame.Type == "PING" ? "PONG" : "ACK",
                    frame.RequestId,
                    null).Serialize();
                await using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true);
                await writer.WriteAsync(response.AsMemory(), cancellation.Token);
                await writer.FlushAsync(cancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                server.Dispose();
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        cancellation.Cancel();
        cancellation.Dispose();
        return ValueTask.CompletedTask;
    }
}
