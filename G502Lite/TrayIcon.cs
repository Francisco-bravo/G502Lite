using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace G502Lite;

internal sealed class TrayIcon : IDisposable
{
    private const int CallbackMessage=0x8001;
    private static readonly uint ShowMessage=RegisterWindowMessage("G502Lite.046D.C08B.ShowWindow.v1");
    private static readonly uint TaskbarCreatedMessage=RegisterWindowMessage("TaskbarCreated");
    private static readonly Guid IconIdentity=new(SHA256.HashData(Encoding.UTF8.GetBytes(StartupRegistration.ExecutablePath.ToUpperInvariant())).AsSpan(0,16));
    private readonly HwndSource source;
    private readonly IntPtr icon;
    private readonly Action show;
    private readonly Action exit;
    private readonly ContextMenu menu=new(){Background=Brushes.White,Foreground=Brushes.Black};
    private readonly MenuItem startupItem=new(){Header="Iniciar con Windows",IsCheckable=true};
    private bool disposed;
    public ContextMenu Menu=>menu;

    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;public IntPtr Window;public uint Id,Flags,Callback;public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Tip;
        public uint State,StateMask;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)] public string InfoTitle;
        public uint InfoFlags;public Guid Guid;public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll",EntryPoint="Shell_NotifyIconW",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint operation,ref NotifyIconData data);
    [DllImport("shell32.dll",EntryPoint="ExtractIconExW",CharSet=CharSet.Unicode)]
    private static extern uint ExtractIconEx(string path,int index,out IntPtr large,out IntPtr small,uint count);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll",EntryPoint="LoadIconW")] private static extern IntPtr LoadIcon(IntPtr instance,IntPtr resource);
    [DllImport("user32.dll")] private static extern IntPtr CopyIcon(IntPtr icon);

    public TrayIcon(Action show,Action exit)
    {
        this.show=show;this.exit=exit;
        source=new HwndSource(new HwndSourceParameters("G502Lite.TrayHost"){WindowStyle=unchecked((int)0x80000000),ExtendedWindowStyle=0x80,Width=0,Height=0});
        source.AddHook(WindowMessage);
        ExtractIconEx(StartupRegistration.ExecutablePath,0,out var large,out var small,1);
        icon=small!=IntPtr.Zero?small:large;
        if(small!=IntPtr.Zero&&large!=IntPtr.Zero)DestroyIcon(large);
        if(icon==IntPtr.Zero)icon=CopyIcon(LoadIcon(IntPtr.Zero,new IntPtr(32512)));
        var open=new MenuItem{Header="Abrir G502 Lite",FontWeight=FontWeights.SemiBold};open.Click+=(_,_)=>show();
        var quit=new MenuItem{Header="Salir"};quit.Click+=(_,_)=>exit();
        startupItem.Click+=(_,_)=>
        {
            try{StartupRegistration.SetEnabled(startupItem.IsChecked);}
            catch(Exception ex){startupItem.IsChecked=!startupItem.IsChecked;MessageBox.Show(ex.Message,"G502 Lite");}
        };
        menu.Items.Add(open);menu.Items.Add(new Separator());menu.Items.Add(startupItem);menu.Items.Add(new Separator());menu.Items.Add(quit);
        menu.Opened+=(_,_)=>{try{startupItem.IsChecked=StartupRegistration.IsEnabled();}catch{startupItem.IsChecked=false;}};
        try{Add();}
        catch{if(icon!=IntPtr.Zero)DestroyIcon(icon);source.Dispose();throw;}
    }
    public static void ShowExisting()=>PostMessage(new IntPtr(0xffff),ShowMessage,IntPtr.Zero,IntPtr.Zero);
    private NotifyIconData Data()=>new(){Size=(uint)Marshal.SizeOf<NotifyIconData>(),Window=source.Handle,Id=1,Guid=IconIdentity,Tip="G502 Lite · Clic para abrir",Info="",InfoTitle=""};
    private void Add()
    {
        var data=Data();data.Flags=1|2|4|0x20|0x80;data.Callback=CallbackMessage;data.Icon=icon;
        if(!ShellNotifyIcon(0,ref data))throw new Win32Exception(Marshal.GetLastWin32Error(),"No se pudo crear el icono de G502 Lite en la bandeja.");
        data.Version=4;ShellNotifyIcon(4,ref data);
    }
    private IntPtr WindowMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(disposed)return IntPtr.Zero;
        if(message==0x11){handled=true;return new IntPtr(1);}
        if(message==0x16&&wParam!=IntPtr.Zero){source.Dispatcher.BeginInvoke(exit);return IntPtr.Zero;}
        if((uint)message==ShowMessage){handled=true;source.Dispatcher.BeginInvoke(show);}
        else if((uint)message==TaskbarCreatedMessage)
        {
            try{Add();}catch{source.Dispatcher.BeginInvoke(show);}
        }
        else if(message==CallbackMessage)
        {
            var action=(int)(lParam.ToInt64()&0xffff);handled=true;
            if(action is 0x202 or 0x203 or 0x400 or 0x401)source.Dispatcher.BeginInvoke(show);
            if(action is 0x205 or 0x7b)
            {
                try{startupItem.IsChecked=StartupRegistration.IsEnabled();}catch{startupItem.IsChecked=false;}
                SetForegroundWindow(source.Handle);menu.Placement=PlacementMode.MousePoint;menu.IsOpen=true;
                PostMessage(source.Handle,0,IntPtr.Zero,IntPtr.Zero);
            }
        }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;menu.IsOpen=false;
        var data=Data();data.Flags=0x20;ShellNotifyIcon(2,ref data);
        if(icon!=IntPtr.Zero)DestroyIcon(icon);source.Dispose();
    }
}
