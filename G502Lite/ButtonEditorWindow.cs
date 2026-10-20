using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using G502Lite.Device;
using Microsoft.Win32;

namespace G502Lite;

internal sealed record BindingChoice(string Name,string Hex)
{
    public override string ToString()=>Name;
}

public sealed class ButtonEditorWindow : Window
{
    private readonly DeviceController device;
    private MemorySnapshot snapshot;
    private readonly Dictionary<int,string> pending=new();
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,16,0,0)};
    private readonly MouseButtonMap map=new();
    private readonly Grid body=new(){Margin=new Thickness(0,18,0,0)};
    private readonly TextBlock selectionTitle=new(){FontSize=23,FontWeight=FontWeights.SemiBold};
    private readonly TextBlock positionText=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue,Margin=new Thickness(0,5,0,24)};
    private readonly TextBlock savedText=new(){TextWrapping=TextWrapping.Wrap,FontSize=17,Margin=new Thickness(0,7,0,24)};
    private readonly ComboBox assignment=new(){Margin=new Thickness(0,8,0,16),HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly TextBlock selectionHelp=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue,Margin=new Thickness(0,6,0,0)};
    private readonly TextBlock pendingText=new(){TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(224,163,212)),Margin=new Thickness(0,22,0,0)};
    private readonly Button topButton=new(){Content="Vista superior",Margin=new Thickness(0,0,8,0)};
    private readonly Button sideButton=new(){Content="Vista lateral"};
    private int selected=1;
    private bool side;
    private bool changingSelection;
    private bool busy;
    private bool closeAfterOperation;
    private static readonly List<BindingChoice> Choices=CreateChoices();
    private readonly ButtonActivityMonitor? activity;
    private readonly TextBlock activityText=new(){Text="Prueba: pulsa un botón del mouse para verlo aquí.",TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(116,245,156)),Margin=new Thickness(0,10,0,0)};
    internal ButtonEditorWindow(DeviceController device,MemorySnapshot snapshot,ButtonActivityMonitor? activity=null)
    {
        this.device=device;this.snapshot=snapshot;
        this.activity=activity;
        Title="G502 Lite · Botones";Width=1160;Height=830;MinWidth=1000;MinHeight=720;
        Background=new SolidColorBrush(Color.FromRgb(11,16,32));Foreground=Brushes.White;FontFamily=new FontFamily("Segoe UI");FontSize=14;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Resources.MergedDictionaries.Add(Application.Current.MainWindow.Resources);
        var root=new Grid{Margin=new Thickness(26)};Content=root;
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var header=new Grid();root.Children.Add(header);
        var heading=new StackPanel();header.Children.Add(heading);
        heading.Children.Add(new TextBlock{Text="Botones del mouse",FontSize=26,FontWeight=FontWeights.SemiBold});
        heading.Children.Add(new TextBlock{Text="Pulsa un punto o su etiqueta para configurar ese botón.",Foreground=Brushes.LightSteelBlue,Margin=new Thickness(0,8,0,0)});
        var views=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
        views.Children.Add(topButton);views.Children.Add(sideButton);header.Children.Add(views);
        topButton.Click+=(_,_)=>SwitchView(false);sideButton.Click+=(_,_)=>SwitchView(true);
        body.ColumnDefinitions.Add(new ColumnDefinition());body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(18)});body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(310)});
        Grid.SetRow(body,1);root.Children.Add(body);
        var picture=new Border{Background=Brushes.Black,CornerRadius=new CornerRadius(16),ClipToBounds=true,Child=map};body.Children.Add(picture);
        var editor=new StackPanel();
        var card=new Border{Style=(Style)FindResource("Card"),Child=editor,VerticalAlignment=VerticalAlignment.Top};Grid.SetColumn(card,2);body.Children.Add(card);
        editor.Children.Add(selectionTitle);editor.Children.Add(positionText);
        editor.Children.Add(new TextBlock{Text="ASIGNACIÓN GUARDADA",FontSize=11,Foreground=Brushes.LightSteelBlue});editor.Children.Add(savedText);
        editor.Children.Add(new TextBlock{Text="Nueva asignación",FontWeight=FontWeights.SemiBold});editor.Children.Add(assignment);editor.Children.Add(selectionHelp);editor.Children.Add(pendingText);
        System.Windows.Automation.AutomationProperties.SetName(assignment,"Nueva asignación del botón seleccionado");
        assignment.SelectionChanged+=(_,_)=>AssignmentChanged();
        map.ButtonSelected+=Select;
        var actions=new WrapPanel{Margin=new Thickness(0,18,0,0)};
        AddAction(actions,"Guardar en el mouse",SaveAsync);AddAction(actions,"Descartar cambios",DiscardAsync);AddAction(actions,"Exportar copia",ExportAsync);AddAction(actions,"Restaurar copia…",RestoreAsync);
        Grid.SetRow(actions,2);root.Children.Add(actions);
        var footer=new StackPanel();footer.Children.Add(status);footer.Children.Add(activityText);Grid.SetRow(footer,3);root.Children.Add(footer);
        if(activity is not null)activity.Activity+=OnButtonActivity;
        Closed+=(_,_)=>{if(activity is not null)activity.Activity-=OnButtonActivity;};
        Closing+=(_,e)=>{if(busy){e.Cancel=true;closeAfterOperation=true;status.Text="Terminando la operación antes de cerrar…";}};
        Render();
    }
    private void Render()
    {
        activity?.UpdateBindings(snapshot);
        var state=OnboardMemory.Decode(snapshot);
        Select(selected);
        status.Foreground=Brushes.LightSteelBlue;
        status.Text=$"Niveles DPI: {string.Join(" / ",state.DpiLevels.Where(x=>x>0))}. Los cambios se aplican al pulsar Guardar en el mouse.";
        if(!state.CrcValid){status.Text="La memoria activa no tiene un CRC válido. Exporta una copia de su estado o restaura una copia válida.";status.Foreground=Brushes.LightSalmon;}
    }
    private void OnButtonActivity(ButtonActivity value,int[] slots)
    {
        string buttons=slots.Length>0?string.Join(" / ",slots.Select(x=>$"G{x}"))+" · ":"";
        string phase=value.Pressed switch{true=>"Pulsado",false=>"Liberado",_=>"Detectado"};
        activityText.Text=$"{buttons}{value.Name} · {phase} · {DateTime.Now:HH:mm:ss}"+(slots.Length>1?" · Acción compartida: no se distingue el botón físico.":"");
        if(value.Pressed!=false)map.Pulse(slots);
    }
    private string Label(int slot,string hex)=>Choices.FirstOrDefault(x=>x.Hex==hex)?.Name??OnboardMemory.Decode(snapshot).Buttons[slot-1].Action;
    private string StoredHex(int slot)=>Convert.ToHexString(snapshot.GetSector(snapshot.ActiveSector).AsSpan(32+(slot-1)*4,4));
    private void Select(int slot)
    {
        selected=slot;side=MouseButtonLayout.Side.Any(x=>x.Slot==slot);
        selectionTitle.Text=$"G{slot}";positionText.Text=MouseButtonLayout.Position(slot);
        var original=StoredHex(slot);var value=pending.GetValueOrDefault(slot,original);
        savedText.Text=Label(slot,original);
        var choices=Choices.ToList();if(!choices.Any(x=>x.Hex==original))choices.Insert(0,new BindingChoice(Label(slot,original)+" · conservar",original));
        changingSelection=true;
        try{assignment.ItemsSource=choices;assignment.SelectedItem=choices.First(x=>x.Hex==value);}
        finally{changingSelection=false;}
        assignment.IsEnabled=slot!=1&&OnboardMemory.Decode(snapshot).CrcValid;
        selectionHelp.Text=slot==1?"El clic izquierdo se conserva.":"Elige una acción. Puedes preparar varios botones antes de guardar.";
        UpdateMap();
    }
    private void SwitchView(bool showSide)
    {
        side=showSide;
        if(!(side?MouseButtonLayout.Side:MouseButtonLayout.Top).Any(x=>x.Slot==selected))Select(side?6:1);
        else UpdateMap();
    }
    private void AssignmentChanged()
    {
        if(changingSelection||assignment.SelectedItem is not BindingChoice choice||selected==1)return;
        if(choice.Hex==StoredHex(selected))pending.Remove(selected);else pending[selected]=choice.Hex;
        UpdateMap();
    }
    private void UpdateMap()
    {
        var labels=Enumerable.Range(1,11).ToDictionary(slot=>slot,slot=>Label(slot,pending.GetValueOrDefault(slot,StoredHex(slot))));
        map.SetState(side,selected,labels,pending.Keys.ToHashSet());
        var active=new SolidColorBrush(Color.FromRgb(97,232,240));var normal=new SolidColorBrush(Color.FromRgb(32,45,70));
        topButton.Background=side?normal:active;topButton.Foreground=side?Brushes.White:Brushes.Black;
        sideButton.Background=side?active:normal;sideButton.Foreground=side?Brushes.Black:Brushes.White;
        pendingText.Text=pending.Count switch{0=>"Sin cambios pendientes.",1=>"Un botón con cambios pendientes de guardar.",_=>$"{pending.Count} botones con cambios pendientes de guardar."};
    }
    private void AddAction(Panel panel,string label,Func<Task> action)
    {
        var button=new Button{Content=label,Margin=new Thickness(0,0,8,8)};
        button.Click+=async(_,_)=>
        {
            if(busy)return;busy=true;panel.IsEnabled=false;body.IsEnabled=false;
            try{await action();}
            catch(Exception ex){status.Text=ex.Message;status.Foreground=Brushes.LightSalmon;}
            finally{busy=false;panel.IsEnabled=true;body.IsEnabled=true;if(closeAfterOperation)Close();}
        };
        panel.Children.Add(button);
    }
    private async Task SaveAsync()
    {
        if(pending.Count==0){status.Text="No hay cambios para guardar.";return;}
        status.Foreground=Brushes.LightSteelBlue;status.Text="Guardando copia previa y verificando la escritura…";
        snapshot=await device.SaveButtonsAsync(snapshot,pending);pending.Clear();Render();
        status.Text="Asignaciones guardadas y verificadas. La copia previa está en la carpeta backups de la aplicación.";
    }
    private Task DiscardAsync(){pending.Clear();Select(selected);status.Text="Cambios descartados. El mouse no se ha modificado.";return Task.CompletedTask;}
    private async Task ExportAsync()
    {
        var dialog=new SaveFileDialog{Title="Exportar copia del mouse",Filter="Copia G502 Lite (*.json)|*.json",FileName=$"g502-copia-{DateTime.Now:yyyyMMdd-HHmmss}.json"};
        if(dialog.ShowDialog(this)!=true)return;
        var backup=await device.BackupMemoryAsync();
        await File.WriteAllTextAsync(dialog.FileName,JsonSerializer.Serialize(backup,new JsonSerializerOptions{WriteIndented=true}));
        status.Text="Copia completa exportada. Los cambios pendientes se conservan en el editor.";
    }
    private async Task RestoreAsync()
    {
        var dialog=new OpenFileDialog{Title="Restaurar configuración activa",Filter="Copia G502 Lite (*.json)|*.json",InitialDirectory=Path.Combine(AppContext.BaseDirectory,"backups")};
        if(dialog.ShowDialog(this)!=true)return;
        var backup=JsonSerializer.Deserialize<MemorySnapshot>(await File.ReadAllTextAsync(dialog.FileName))??throw new IOException("Copia vacía.");
        status.Text="Restaurando la configuración activa y comprobando lectura…";
        snapshot=await device.RestoreActiveMemoryAsync(snapshot,backup);pending.Clear();Render();status.Text="Configuración activa restaurada y verificada.";
    }
    private static List<BindingChoice> CreateChoices()
    {
        var choices=new List<BindingChoice>();
        void Add(string label,string hex)=>choices.Add(new(label,hex));
        Add("Clic izquierdo","80010001");Add("Clic derecho","80010002");Add("Clic central","80010004");Add("Atrás","80010008");Add("Adelante","80010010");
        Add("Pista anterior","800300B6");Add("Reproducir / pausa","800300CD");Add("Pista siguiente","800300B5");Add("Silenciar","800300E2");Add("Subir volumen","800300E9");Add("Bajar volumen","800300EA");
        Add("Subir DPI","90030000");Add("Bajar DPI","90040000");Add("Alternar DPI","90050000");Add("DPI Shift","90070000");Add("DPI predeterminado","90060000");Add("Rueda izquierda","90010000");Add("Rueda derecha","90020000");Add("G-Shift","900B0000");Add("Sin acción","8000FFFF");
        Add("Copiar · Ctrl+C","80020106");Add("Pegar · Ctrl+V","80020119");Add("Cortar · Ctrl+X","8002011B");Add("Deshacer · Ctrl+Z","8002011D");Add("Rehacer · Ctrl+Y","8002011C");Add("Seleccionar todo · Ctrl+A","80020104");Add("Guardar · Ctrl+S","80020116");
        Add("Enter","80020028");Add("Escape","80020029");Add("Retroceso","8002002A");Add("Tabulación","8002002B");Add("Espacio","8002002C");
        for(int i=0;i<26;i++)Add(((char)('A'+i)).ToString(),$"800200{4+i:X2}");
        for(int i=0;i<12;i++)Add($"F{i+1}",$"800200{0x3a+i:X2}");
        return choices;
    }
}
