using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using G502Lite.Device;

namespace G502Lite;

public partial class MainWindow : Window
{
    private DeviceController? _controller;
    private DeviceState? _state;
    private readonly CancellationTokenSource _lifetime = new();
    private Task _pending = Task.CompletedTask;
    private bool _busy;
    private bool _closing;
    private bool _closed;
    private bool _exitRequested;
    private bool _waitingForChild;
    private bool _syncStartup;
    private bool _trayAvailable=true;
    private WindowState _restoreState=WindowState.Normal;
    private readonly System.Windows.Threading.DispatcherTimer _connectionMonitor=new(){Interval=TimeSpan.FromSeconds(2)};
    private bool _recoverAfterDisconnect;
    private bool _monitoringStarted;
    private ButtonActivityMonitor? _activity;
    private int _activityPresses;

    public MainWindow()
    {
        InitializeComponent();
        Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/G502Lite;component/Assets/G502Lite.ico"));
        Height=Math.Max(MinHeight,Math.Min(Height,SystemParameters.WorkArea.Height-30));
        Loaded += (_, _) => StartMonitoring();
        _connectionMonitor.Tick+=(_,_)=>
        {
            if(_closing)return;
            if(_controller is {IsConnected:false})_recoverAfterDisconnect=true;
            if((_recoverAfterDisconnect||_controller is null)&&!_busy)Start(ConnectAsync);
        };
        Closing += OnClosing;
        StateChanged+=(_,_)=>
        {
            if(WindowState==WindowState.Minimized&&!_exitRequested)HideToTray();
            else if(WindowState!=WindowState.Minimized)_restoreState=WindowState;
        };
        Activated+=(_,_)=>RefreshStartupOption();
        RefreshStartupOption();
    }

    public void StartMonitoring()
    {
        if(_monitoringStarted||_closing)return;
        _monitoringStarted=true;
        try
        {
            _activity=new ButtonActivityMonitor(()=>!_closing&&(IsVisible||OwnedWindows.Cast<Window>().Any(w=>w.IsVisible)));
            _activity.Activity+=ShowButtonActivity;
        }
        catch(Exception ex){ActivityText.Text=$"Indicador no disponible: {ex.Message}";}
        Start(ConnectAsync);_connectionMonitor.Start();
    }

    private void ShowButtonActivity(ButtonActivity value,int[] slots)
    {
        if(value.Pressed!=false)_activityPresses++;
        string buttons=slots.Length>0?string.Join(" / ",slots.Select(x=>$"G{x}"))+" · ":"";
        string phase=value.Pressed switch{true=>"Pulsado",false=>"Liberado",_=>"Detectado"};
        ActivityText.Text=$"{buttons}{value.Name} · {phase}\n{_activityPresses} pulsaciones detectadas · {DateTime.Now:HH:mm:ss}";
        ActivityText.Foreground=value.Pressed==false?Brushes.LightSteelBlue:new SolidColorBrush(Color.FromRgb(97,232,240));
    }
    private async Task RefreshActivityBindingsAsync()
    {
        if(_activity is null||_controller is null)return;
        if(_state?.OnboardMode!="Memoria integrada"){_activity.UpdateBindings(null);return;}
        try{_activity.UpdateBindings(await _controller.BackupMemoryAsync(_lifetime.Token));}
        catch(Exception ex)when(ex is IOException or NotSupportedException){ActivityText.Text="Lectura de botones físicos activa; no se pudo asociar la acción con un G1–G11.";}
    }

    private void Start(Func<Task> action)
    {
        if (_busy || _closing) return;
        _pending = RunAsync(action);
    }

    private async Task RunAsync(Func<Task> action)
    {
        _busy = true;
        SetControls();
        ConnectionLabel.Text = "En curso…";
        StatusText.Text = "Comunicando con el mouse…";
        StatusText.Foreground = Brushes.LightSteelBlue;
        try { await action(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ex is IOException or TimeoutException)
            {
                _state = null;
                ClearReadings();
            }
            StatusText.Text = $"No se pudo completar la operación: {ex.Message}";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 171, 171));
        }
        finally
        {
            _busy = false;
            if (!_closing) SetControls();
        }
    }

    private async Task ConnectAsync()
    {
        if(_controller is {IsConnected:false})_recoverAfterDisconnect=true;
        if (_controller is not null)
        {
            await _controller.DisposeAsync();
            _controller = null;
            _state = null;
            ClearReadings();
        }
        _controller = await DeviceController.ConnectAsync(_lifetime.Token);
        if(_recoverAfterDisconnect)
        {
            _recoverAfterDisconnect=false;
            var state=await _controller.RefreshAsync(_lifetime.Token);
            if(state.OnboardMode=="Memoria integrada"&&!state.GHubRunning)
            {
                ShowState(await _controller.ReloadActiveMemoryAsync(_lifetime.Token));
                Success("USB recuperado. Se recargó la configuración activa sin reescribir la memoria.");
                StartupLog.Write("USB recuperado; configuración activa recargada sin escritura de memoria.");
            }
            else
            {
                ShowState(state);
                Success("USB recuperado. El modo actual u otro controlador impide recargar los botones automáticamente.");
            }
            await RefreshActivityBindingsAsync();return;
        }
        ShowState(await _controller.RefreshAsync(_lifetime.Token));
        await RefreshActivityBindingsAsync();
        Success("Mouse conectado. Estado leído directamente del dispositivo.");
    }

    private void ShowState(DeviceState state)
    {
        _state = state;
        ModelText.Text = state.ModelName;
        CurrentDpiText.Text = state.Dpi.ToString(CultureInfo.InvariantCulture);
        DpiInput.Text = state.Dpi.ToString(CultureInfo.InvariantCulture);
        CurrentRateText.Text = state.ReportRateHz.HasValue ? $"{state.ReportRateHz} Hz" : "No disponible";
        ModeText.Text = string.IsNullOrWhiteSpace(state.OnboardMode) ? "Conexión directa USB" : state.OnboardMode;
        DpiRangeText.Text = $"{state.MinimumDpi}–{state.MaximumDpi} DPI · Paso: {state.DpiStep}";
        DpiLevelsText.Text = $"DPI predeterminado: {state.DefaultDpi}";
        RateCombo.ItemsSource = state.SupportedReportRatesHz;
        RateCombo.SelectedItem = state.ReportRateHz;
        LightingHelpText.Text = state.SupportsLighting ? "Elige un efecto y aplícalo al mouse." : "Iluminación no disponible mediante este controlador.";
        if(state.Lighting is { } effect)
        {
            LightingModeCombo.SelectedIndex=effect.Mode switch{LightingMode.Fixed=>0,LightingMode.Cycle=>1,_=>2};
            if(effect.ColorHex is not null)ColorInput.Text=effect.ColorHex;
            if(effect.CycleMilliseconds is int ms)CycleSecondsInput.Text=(ms/1000m).ToString("0.###",CultureInfo.CurrentCulture);
            LightingHelpText.Text=effect.Mode==LightingMode.Cycle?$"Actual: multicolor gradual · {effect.CycleMilliseconds/1000m:0.###} s por vuelta.":effect.Mode==LightingMode.Fixed?$"Actual: color fijo {effect.ColorHex}.":"Actual: luces apagadas.";
        }
        GHubWarningText.Visibility = state.GHubRunning ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearReadings()
    {
        CurrentDpiText.Text = "—";
        CurrentRateText.Text = "—";
        ModeText.Text = "Conecta el mouse para leer su estado.";
        GHubWarningText.Visibility = Visibility.Collapsed;
    }

    private void SetControls()
    {
        bool connected = _controller is not null && _state is not null;
        bool ready = connected && !_busy;
        ConnectionLabel.Text = connected ? "Conectado" : "Desconectado";
        ConnectionDot.Fill = new SolidColorBrush(connected ? Color.FromRgb(97, 232, 240) : Color.FromRgb(152, 169, 196));
        ConnectButton.Content = connected ? "Reconectar" : "Conectar";
        ConnectButton.IsEnabled = !_busy;
        RefreshButton.IsEnabled = ready;
        DpiInput.IsEnabled = ready;
        QuickDpiPanel.IsEnabled = ready;
        ApplyDpiButton.IsEnabled = ready;
        bool rateReady = ready && _state!.SupportedReportRatesHz.Count > 0;
        RateCombo.IsEnabled = rateReady;
        ApplyRateButton.IsEnabled = rateReady;
        ButtonsButton.IsEnabled=ready;
        RecoverButtonsButton.IsEnabled=ready;
        bool lightsReady = ready && _state!.SupportsLighting;
        ColorInput.IsEnabled = lightsReady;
        LightingModeCombo.IsEnabled=lightsReady;
        CycleModeItem.IsEnabled=lightsReady&&_state!.SupportsColorCycle;
        CycleSecondsInput.IsEnabled=lightsReady&&_state!.SupportsColorCycle;
        ApplyLightingButton.IsEnabled=lightsReady&&(LightingModeCombo.SelectedIndex!=1||_state!.SupportsColorCycle);
    }

    private void Success(string message)
    {
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(180, 218, 211));
    }

    private void Connect_Click(object sender, RoutedEventArgs e) => Start(ConnectAsync);
    private void RecoverButtons_Click(object sender,RoutedEventArgs e)=>Start(async()=>
    {
        ShowState(await _controller!.ReloadActiveMemoryAsync(_lifetime.Token));
        Success("Configuración activa recargada. Prueba los botones; la memoria guardada no se modificó.");
    });
    private void Refresh_Click(object sender, RoutedEventArgs e) => Start(async () =>
    {
        ShowState(await _controller!.RefreshAsync(_lifetime.Token));
        Success("Estado actualizado desde el mouse.");
    });
    private void Buttons_Click(object sender,RoutedEventArgs e)=>Start(async()=>
    {
        var backup=await _controller!.BackupMemoryAsync(_lifetime.Token);
        var editor=new ButtonEditorWindow(_controller,backup,_activity){Owner=this};
        editor.ShowDialog();
        ShowState(await _controller.RefreshAsync(_lifetime.Token));
        Success("Estado actualizado tras revisar botones.");
    });

    private void QuickDpi_Click(object sender, RoutedEventArgs e)
    {
        DpiInput.Text = ((Button)sender).Tag.ToString();
        Success("DPI seleccionado. Pulsa Aplicar DPI para enviarlo al mouse.");
    }

    private void ApplyDpi_Click(object sender, RoutedEventArgs e) => Start(async () =>
    {
        if (!int.TryParse(DpiInput.Text.Trim(), out int dpi)) throw new ArgumentException("Introduce un número entero de DPI.");
        if (_state is null || dpi < _state.MinimumDpi || dpi > _state.MaximumDpi)
            throw new ArgumentException($"El DPI debe estar entre {_state?.MinimumDpi} y {_state?.MaximumDpi}.");
        ShowState(await _controller!.SetDpiAsync(dpi, _lifetime.Token));
        Success($"DPI aplicado. Lectura actual: {_state!.Dpi} DPI.");
    });

    private void ApplyRate_Click(object sender, RoutedEventArgs e) => Start(async () =>
    {
        if (RateCombo.SelectedItem is not int hz) throw new ArgumentException("Selecciona una frecuencia compatible.");
        ShowState(await _controller!.SetReportRateAsync(hz, _lifetime.Token));
        Success($"Frecuencia aplicada. Lectura actual: {_state!.ReportRateHz} Hz.");
    });

    private void ApplyColor_Click(object sender, RoutedEventArgs e) => Start(async () =>
    {
        string hex = ColorInput.Text.Trim();
        if (hex.StartsWith('#')) hex = hex[1..];
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            throw new ArgumentException("Introduce un color de seis dígitos, por ejemplo #61E8F0.");
        ShowState(await _controller!.SetLightingAsync($"#{hex.ToUpperInvariant()}", false, _lifetime.Token));
        Success(_state!.OnboardMode=="Memoria integrada"?"Iluminación guardada y verificada en la memoria del mouse.":"Color fijo enviado al mouse.");
    });

    private void LightingMode_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(ColorSettingsPanel is null||CycleSettingsPanel is null)return;
        ColorSettingsPanel.Visibility=LightingModeCombo.SelectedIndex==0?Visibility.Visible:Visibility.Collapsed;
        CycleSettingsPanel.Visibility=LightingModeCombo.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;
        if(ApplyLightingButton is not null)ApplyLightingButton.IsEnabled=!_busy&&_state?.SupportsLighting==true&&(LightingModeCombo.SelectedIndex!=1||_state.SupportsColorCycle);
    }
    private void ApplyLighting_Click(object sender,RoutedEventArgs e)
    {
        if(LightingModeCombo.SelectedIndex==0){ApplyColor_Click(sender,e);return;}
        if(LightingModeCombo.SelectedIndex==2){LightsOff_Click(sender,e);return;}
        Start(async()=>
        {
            var ms=LightingEffect.ParseCycleMilliseconds(CycleSecondsInput.Text);
            ShowState(await _controller!.SetColorCycleAsync(ms,_lifetime.Token));
            Success(_state!.OnboardMode=="Memoria integrada"?$"Multicolor gradual guardado: una vuelta cada {ms/1000m:0.###} s. Funciona con la aplicación cerrada.":$"Multicolor gradual enviado: una vuelta cada {ms/1000m:0.###} s.");
        });
    }

    private void LightsOff_Click(object sender, RoutedEventArgs e) => Start(async () =>
    {
        ShowState(await _controller!.SetLightingAsync("#000000", true, _lifetime.Token));
        Success("Orden de apagado de iluminación enviada al mouse.");
    });

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closed) return;
        if(!_exitRequested&&_trayAvailable){e.Cancel=true;HideToTray();return;}
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        _connectionMonitor.Stop();
        _activity?.Dispose();
        IsEnabled = false;
        _lifetime.Cancel();
        try
        {
            await _pending;
            if (_controller is not null) await _controller.DisposeAsync();
        }
        catch { /* Closing must still release the UI if the device has disappeared. */ }
        finally
        {
            _lifetime.Dispose();
            _closed = true;
            Close();
            Application.Current.Shutdown();
        }
    }
    public void SetTrayAvailable(bool available){_trayAvailable=available;HideToTrayButton.IsEnabled=available;}
    public void SetTrayMenu(ContextMenu menu)=>HideToTrayButton.ContextMenu=menu;
    public void HideToTray()
    {
        if(!_trayAvailable)return;
        Hide();ShowInTaskbar=false;
    }
    public void ShowFromTray()
    {
        if(_closing)return;
        ShowInTaskbar=true;Show();WindowState=_restoreState;Activate();
        var child=OwnedWindows.Cast<Window>().FirstOrDefault(x=>x.IsVisible);
        if(child is not null){child.WindowState=WindowState.Normal;child.Activate();}
    }
    public void AllowSessionExit()=>_exitRequested=true;
    public void RequestExit()
    {
        if(_waitingForChild||_closing)return;
        _exitRequested=true;
        var child=OwnedWindows.Cast<Window>().FirstOrDefault(x=>x.IsVisible);
        if(child is not null)
        {
            _waitingForChild=true;
            child.Closed+=(_,_)=>{_waitingForChild=false;Dispatcher.BeginInvoke(RequestExit);};
            child.Close();return;
        }
        Close();
    }
    private void HideToTray_Click(object sender,RoutedEventArgs e)=>HideToTray();
    private void RefreshStartupOption()
    {
        _syncStartup=true;
        try{StartupCheckBox.IsChecked=StartupRegistration.IsEnabled();}
        catch(Exception ex){StatusText.Text=$"No se pudo consultar el inicio automático: {ex.Message}";}
        finally{_syncStartup=false;}
    }
    private void Startup_Changed(object sender,RoutedEventArgs e)
    {
        if(_syncStartup||StartupCheckBox is null)return;
        try
        {
            bool enabled=StartupCheckBox.IsChecked==true;StartupRegistration.SetEnabled(enabled);
            Success(enabled?"Inicio con Windows activado: se abrirá solo el icono de la bandeja.":"Inicio con Windows desactivado.");
        }
        catch(Exception ex){RefreshStartupOption();StatusText.Text=$"No se pudo cambiar el inicio automático: {ex.Message}";}
    }
}
