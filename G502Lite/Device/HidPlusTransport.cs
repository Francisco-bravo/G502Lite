using System.Collections.Concurrent;

namespace G502Lite.Device;

public sealed class HidPlusTransport : IAsyncDisposable
{
    public const byte SoftwareId=0x09;
    private readonly FileStream stream;
    private readonly HidInterface endpoint;
    private readonly CancellationTokenSource lifetime=new();
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly ConcurrentDictionary<ushort,TaskCompletionSource<byte[]>> pending=new();
    private readonly Task reader;
    private bool disposed;
    private bool faulted;
    public HidInterface Endpoint=>endpoint;
    public HidPlusTransport(HidInterface endpoint)
    {
        if(endpoint.InputLength!=20||endpoint.OutputLength!=20||endpoint.UsagePage<0xff00) throw new ArgumentException("Se requiere la interfaz HID++ de 20 bytes.");
        this.endpoint=endpoint;stream=NativeHid.Open(endpoint);reader=ReadLoopAsync();
    }
    public async Task<byte[]> RequestAsync(byte feature,byte function,byte[]? parameters=null,CancellationToken token=default)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(faulted)throw new IOException("La conexión requiere reconectar el mouse desde la aplicación.");
        if(function>15||(parameters?.Length??0)>16) throw new ArgumentOutOfRangeException(nameof(parameters));
        await gate.WaitAsync(token);
        try
        {
            var command=(byte)((function<<4)|SoftwareId);var key=(ushort)((feature<<8)|command);
            var completion=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            if(!pending.TryAdd(key,completion)) throw new InvalidOperationException("Hay una petición pendiente.");
            try
            {
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(2));
                var packet=new byte[20];packet[0]=0x11;packet[1]=0xff;packet[2]=feature;packet[3]=command;parameters?.CopyTo(packet,4);
                try
                {
                    await stream.WriteAsync(packet,deadline.Token);
                    return await completion.Task.WaitAsync(deadline.Token);
                }
                catch(OperationCanceledException) when(!token.IsCancellationRequested)
                {
                    faulted=true;lifetime.Cancel();
                    throw new IOException("El mouse no respondió. Pulsa Reconectar antes de repetir la operación.");
                }
            }
            catch(TimeoutException) { throw new IOException("El mouse no respondió. Desconecta y vuelve a conectar desde la aplicación antes de repetir."); }
            finally { pending.TryRemove(key,out _); }
        }
        finally { gate.Release(); }
    }
    private async Task ReadLoopAsync()
    {
        var buffer=new byte[endpoint.InputLength];
        try
        {
            while(!lifetime.IsCancellationRequested)
            {
                var n=await stream.ReadAsync(buffer,lifetime.Token);
                if(n==0) throw new IOException("El mouse se desconectó.");
                if(n<4||buffer[1]!=0xff||buffer[0]!=0x11) continue;
                var packet=buffer.AsSpan(0,n).ToArray();
                if(packet[2]==0xff&&n>=7)
                {
                    var errorKey=(ushort)((packet[3]<<8)|packet[4]);
                    if(pending.TryGetValue(errorKey,out var error)) error.TrySetException(new IOException($"El dispositivo rechazó la operación HID++ (0x{packet[5]:X2})."));
                    continue;
                }
                var key=(ushort)((packet[2]<<8)|packet[3]);
                if((packet[3]&15)==SoftwareId&&pending.TryGetValue(key,out var completion)) completion.TrySetResult(packet);
            }
        }
        catch(OperationCanceledException) when(lifetime.IsCancellationRequested) {}
        catch(Exception e) { faulted=true;foreach(var completion in pending.Values) completion.TrySetException(new IOException("Se perdió la conexión con el G502.",e)); }
    }
    public async ValueTask DisposeAsync()
    {
        if(disposed)return;disposed=true;lifetime.Cancel();stream.Dispose();
        foreach(var completion in pending.Values)completion.TrySetCanceled();
        try{await reader.WaitAsync(TimeSpan.FromSeconds(2));}catch{}
        lifetime.Dispose();
    }
}
