using System.Windows;

namespace G502Lite;
public partial class App : Application
{
    private Mutex? instance;
    private TrayIcon? tray;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if(StartupRegistration.IsStartupCommand(e.Args))
        {
            var code=await StartupRegistration.RunCommandAsync(e.Args);Shutdown(code);return;
        }
        bool startHidden=e.Args.Length==1&&e.Args[0]=="--tray";
        StartupLog.Write($"Inicio: bandeja={startHidden}; ejecutable={StartupRegistration.ExecutablePath}");
        AppDomain.CurrentDomain.UnhandledException+=(_,eventArgs)=>StartupLog.Write($"Error no controlado: {eventArgs.ExceptionObject}");
        DispatcherUnhandledException+=(_,eventArgs)=>StartupLog.Write($"Error de interfaz: {eventArgs.Exception}");
        instance=new Mutex(true,@"Local\G502Lite-046D-C08B",out bool firstInstance);
        if(!firstInstance)
        {
            StartupLog.Write("Ya existe una instancia.");
            if(e.Args.Length==0)TrayIcon.ShowExisting();
            instance.Dispose();instance=null;Shutdown(2);return;
        }
        if (e.Args.Length > 0&&!startHidden)
        {
            Environment.ExitCode = await DiagnosticCli.RunAsync(e.Args);
            Shutdown(Environment.ExitCode);
            return;
        }
        var window = new MainWindow();
        MainWindow = window;
        try{tray=new TrayIcon(window.ShowFromTray,window.RequestExit);window.SetTrayMenu(tray.Menu);StartupLog.Write("Icono de bandeja registrado.");}
        catch(Exception ex){StartupLog.Write($"Error al crear la bandeja: {ex}");window.SetTrayAvailable(false);window.Show();MessageBox.Show(window,ex.Message,"G502 Lite");return;}
        if(!startHidden)window.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        StartupLog.Write($"Salida: código={e.ApplicationExitCode}");
        tray?.Dispose();
        if(instance is not null){instance.ReleaseMutex();instance.Dispose();}
        base.OnExit(e);
    }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if(MainWindow is MainWindow window)window.AllowSessionExit();
        base.OnSessionEnding(e);
    }
}
