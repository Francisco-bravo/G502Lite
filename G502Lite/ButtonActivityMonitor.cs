using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using G502Lite.Device;

namespace G502Lite;

internal sealed class ButtonActivityMonitor : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Registration { public ushort Page,Usage;public uint Flags;public IntPtr Target; }
    [StructLayout(LayoutKind.Sequential)] private struct Header { public uint Type,Size;public IntPtr Device;public UIntPtr WParam; }
    [DllImport("user32.dll",SetLastError=true)] private static extern bool RegisterRawInputDevices(Registration[] devices,uint count,uint size);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll",SetLastError=true)] private static extern uint GetRawInputData(IntPtr input,uint command,IntPtr data,ref uint size,uint headerSize);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="GetRawInputDeviceInfoW")] private static extern uint DeviceName(IntPtr device,uint command,StringBuilder? name,ref uint size);
    private readonly HwndSource source;
    private readonly Func<bool> enabled;
    private readonly Dictionary<IntPtr,bool> devices=new();
    private readonly Dictionary<IntPtr,IReadOnlySet<ushort>> consumers=new();
    private readonly Dictionary<IntPtr,G502KeyboardActivity> keyboards=new();
    private MemorySnapshot? snapshot;
    private bool disposed;
    private readonly HoldClick holdClick=new(()=>TripleClick.ExecuteSingle());
    private readonly System.Windows.Threading.DispatcherTimer repeatTimer=new(){Interval=TimeSpan.FromMilliseconds(HoldClick.IntervalMilliseconds)};
    public event Action<ButtonActivity,int[]>? Activity;
    public int ClicksPerSecond { get; private set; }=ClickSpeedSettings.Load();
    public void SetClickSpeed(int speed)
    {
        var interval=ClickSpeedSettings.Interval(speed);
        ClickSpeedSettings.Save(speed);
        StopRepeat();
        ClicksPerSecond=speed;repeatTimer.Interval=interval;
    }
    public ButtonActivityMonitor(Func<bool> enabled)
    {
        this.enabled=enabled;
        repeatTimer.Interval=ClickSpeedSettings.Interval(ClicksPerSecond);
        repeatTimer.Tick+=(_,_)=>
        {
            // Backup release check, also fails closed on an inaccessible desktop.
            // It can only cancel: activation still requires Raw Input from this mouse.
            if(GetAsyncKeyState(0x86)>=0){StopRepeat();return;}
            try{holdClick.Tick();}
            catch(Exception ex){StopRepeat();Activity?.Invoke(new(HoldClick.Binding,ex.Message,false),Array.Empty<int>());}
        };
        source=new HwndSource(new HwndSourceParameters("G502Lite.ButtonActivity"){ParentWindow=new IntPtr(-3),Width=0,Height=0,WindowStyle=0});
        source.AddHook(WndProc);
        if(!RegisterRawInputDevices(Registrations(0x2100,source.Handle),3,(uint)Marshal.SizeOf<Registration>()))
        {var error=Marshal.GetLastWin32Error();source.Dispose();throw new Win32Exception(error,"No se pudo iniciar el indicador de pulsaciones.");}
    }
    private static Registration[] Registrations(uint flags,IntPtr target)=>new[]
    {
        new Registration{Page=1,Usage=2,Flags=flags,Target=target},
        new Registration{Page=0x0c,Usage=1,Flags=flags,Target=target},
        new Registration{Page=1,Usage=6,Flags=flags,Target=target}
    };
    private void StopRepeat(){repeatTimer.Stop();holdClick.Stop();}
    public void UpdateBindings(MemorySnapshot? value){StopRepeat();snapshot=value;}
    private bool IsG502(IntPtr device)
    {
        if(devices.TryGetValue(device,out var known))return known;
        uint length=0;
        if(DeviceName(device,0x20000007,null,ref length)==uint.MaxValue||length==0||length>4096)return false;
        var text=new StringBuilder((int)length+1);
        bool result=DeviceName(device,0x20000007,text,ref length)!=uint.MaxValue&&text.ToString().Contains("vid_046d&pid_c08b",StringComparison.OrdinalIgnoreCase);
        devices[device]=result;return result;
    }
    private void Publish(ButtonActivity value)
    {
        var slots=ButtonActivityDecoder.MatchingSlots(snapshot,value.Binding);
        if(value.Binding==HoldClick.Binding)
        {
            try
            {
                holdClick.Read(value,slots);
                if(holdClick.Active)repeatTimer.Start();else repeatTimer.Stop();
            }
            catch(Exception ex){StopRepeat();Activity?.Invoke(value with{Name=ex.Message},slots);return;}
        }
        if(TripleClick.ShouldRun(value,slots))
        {
            try{TripleClick.Execute();}
            catch(Exception ex){Activity?.Invoke(value with{Name=ex.Message},slots);return;}
        }
        if(enabled())Activity?.Invoke(value.Binding switch
        {
            TripleClick.Binding=>value with{Name=TripleClick.Name},
            HoldClick.Binding=>value with{Name=HoldClick.Name},
            _=>value
        },slots);
    }
    private IntPtr WndProc(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(disposed)return IntPtr.Zero;
        if(message==0xfe){StopRepeat();devices.Remove(lParam);consumers.Remove(lParam);keyboards.Remove(lParam);return IntPtr.Zero;}
        if(message!=0xff)return IntPtr.Zero;
        if(!enabled()&&ButtonActivityDecoder.MatchingSlots(snapshot,TripleClick.Binding).Length==0&&ButtonActivityDecoder.MatchingSlots(snapshot,HoldClick.Binding).Length==0){StopRepeat();consumers.Clear();keyboards.Clear();return IntPtr.Zero;}
        uint headerSize=(uint)Marshal.SizeOf<Header>(),size=0;
        if(GetRawInputData(lParam,0x10000003,IntPtr.Zero,ref size,headerSize)==uint.MaxValue||size<headerSize||size>16384)return IntPtr.Zero;
        var memory=Marshal.AllocHGlobal((int)size);
        try
        {
            if(GetRawInputData(lParam,0x10000003,memory,ref size,headerSize)==uint.MaxValue)return IntPtr.Zero;
            var header=Marshal.PtrToStructure<Header>(memory);
            if(!IsG502(header.Device))return IntPtr.Zero;
            var bytes=new byte[size-headerSize];Marshal.Copy(memory+(int)headerSize,bytes,0,bytes.Length);
            if(header.Type==0&&bytes.Length>=24)
            {
                foreach(var value in ButtonActivityDecoder.Mouse(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)),BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(6))))Publish(value);
            }
            else if(header.Type==1&&bytes.Length>=16)
            {
                if(!keyboards.TryGetValue(header.Device,out var keyboard)){keyboard=new();keyboards[header.Device]=keyboard;}
                var value=keyboard.Read(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6)),BinaryPrimitives.ReadUInt16LittleEndian(bytes),BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)));
                if(value is not null)Publish(value);
            }
            else if(header.Type==2&&bytes.Length>=8)
            {
                uint reportSize=BinaryPrimitives.ReadUInt32LittleEndian(bytes),count=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
                if(reportSize!=5||count>(bytes.Length-8)/5)return IntPtr.Zero;
                for(int i=0;i<count;i++)
                {
                    var current=ButtonActivityDecoder.ConsumerUsages(bytes.AsSpan(8+i*5,5));
                    if(current is null)continue;
                    var previous=consumers.GetValueOrDefault(header.Device)??new HashSet<ushort>();
                    foreach(var code in current.Except(previous))Publish(ButtonActivityDecoder.Consumer(code,true));
                    foreach(var code in previous.Except(current))Publish(ButtonActivityDecoder.Consumer(code,false));
                    consumers[header.Device]=current;
                }
            }
        }
        catch(Exception ex){StopRepeat();StartupLog.Write($"Indicador de pulsaciones: {ex.Message}");}
        finally{Marshal.FreeHGlobal(memory);}
        // Preserve Windows' normal input processing, including WM_INPUT cleanup.
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        StopRepeat();
        RegisterRawInputDevices(Registrations(1,IntPtr.Zero),3,(uint)Marshal.SizeOf<Registration>());
        source.Dispose();devices.Clear();consumers.Clear();keyboards.Clear();
    }
}
