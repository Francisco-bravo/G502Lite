using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace G502Lite;

internal sealed record MouseHotspot(int Slot,double X,double Y,double LabelX,double LabelY);
internal static class MouseButtonLayout
{
    // Physical ordering confirmed against the installed G502 metadata: G7/G8
    // are lower/upper DPI controls; the side controls are G6, G5, G4 top-down.
    public static readonly MouseHotspot[] Top=
    {
        new(1,280,138,18,96),new(2,384,138,460,96),new(3,338,182,250,18),
        new(8,240,179,18,156),new(11,318,199,18,210),new(7,240,217,18,264),
        new(10,359,199,460,200),new(9,341,295,460,280)
    };
    public static readonly MouseHotspot[] Side=
    {
        new(6,317,267,28,210),new(5,358,298,28,290),new(4,369,375,28,370)
    };
    public static string Position(int slot)=>slot switch
    {
        1=>"Clic principal",2=>"Clic secundario",3=>"Clic de la rueda",
        4=>"Lateral trasero",5=>"Lateral central",6=>"Lateral delantero",
        7=>"Control DPI inferior",8=>"Control DPI superior",9=>"Botón superior",
        10=>"Rueda hacia la derecha",11=>"Rueda hacia la izquierda",_=>"Botón"
    };
}

internal sealed class MouseButtonMap : UserControl
{
    private readonly Canvas canvas=new(){Width=660,Height=608,Background=Brushes.Black};
    private IReadOnlyDictionary<int,string> labels=new Dictionary<int,string>();
    private IReadOnlySet<int> changed=new HashSet<int>();
    private bool side;
    private int selected;
    public event Action<int>? ButtonSelected;
    public MouseButtonMap()
    {
        Content=new Viewbox{Child=canvas,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.Both};
        MinHeight=320;
    }
    public void SetState(bool side,int selected,IReadOnlyDictionary<int,string> labels,IReadOnlySet<int> changed)
    {
        this.side=side;this.selected=selected;this.labels=labels;this.changed=changed;Draw();
    }
    private void Draw()
    {
        canvas.Children.Clear();
        var image=new Image{Width=660,Height=608,Stretch=Stretch.Fill,IsHitTestVisible=false,
            Source=new BitmapImage(new Uri($"pack://application:,,,/G502Lite;component/Assets/mouse-{(side?"side":"top")}.png"))};
        RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);canvas.Children.Add(image);
        foreach(var item in side?MouseButtonLayout.Side:MouseButtonLayout.Top)
        {
            var active=selected==item.Slot;
            var color=active?Color.FromRgb(97,232,240):changed.Contains(item.Slot)?Color.FromRgb(224,163,212):Color.FromRgb(168,187,211);
            var brush=new SolidColorBrush(color);
            var start=new Point(item.LabelX<200?item.LabelX+182:item.LabelX>400?item.LabelX:item.LabelX+88,item.LabelY+22);
            var elbow=new Point(item.LabelX<200?224:item.LabelX>400?438:item.X,start.Y);
            var line=new Polyline{Stroke=brush,StrokeThickness=active?1.7:1,Opacity=active?0.95:0.5,IsHitTestVisible=false,
                Points=new PointCollection{start,elbow,new Point(item.X,item.Y)}};
            canvas.Children.Add(line);
            var label=labels.GetValueOrDefault(item.Slot,"Sin lectura");
            var labelContent=new StackPanel();
            labelContent.Children.Add(new TextBlock{Text=$"G{item.Slot}"+(changed.Contains(item.Slot)?" · pendiente":""),FontSize=10,Foreground=brush,Margin=new Thickness(0,0,0,3)});
            labelContent.Children.Add(new TextBlock{Text=label,FontSize=13,FontWeight=FontWeights.SemiBold,Foreground=active?brush:Brushes.White,TextWrapping=TextWrapping.Wrap,MaxHeight=40});
            var text=new Button{Content=labelContent,Width=182,Padding=new Thickness(7,5,7,5),HorizontalContentAlignment=HorizontalAlignment.Left,
                Background=active?new SolidColorBrush(Color.FromRgb(18,49,57)):new SolidColorBrush(Color.FromRgb(11,17,25)),
                ToolTip=$"G{item.Slot} · {MouseButtonLayout.Position(item.Slot)}: {label}"};
            text.Click+=(_,_)=>ButtonSelected?.Invoke(item.Slot);
            System.Windows.Automation.AutomationProperties.SetName(text,$"Configurar G{item.Slot}: {label}");
            Canvas.SetLeft(text,item.LabelX);Canvas.SetTop(text,item.LabelY);canvas.Children.Add(text);
        }
        // Place targets above every leader line and label, so they remain clickable.
        foreach(var item in side?MouseButtonLayout.Side:MouseButtonLayout.Top)
        {
            var active=selected==item.Slot;
            var point=new Button{Width=26,Height=26,Padding=new Thickness(0),Background=Brushes.Transparent,Cursor=Cursors.Hand,
                ToolTip=$"G{item.Slot} · {labels.GetValueOrDefault(item.Slot)}"};
            var circle=new Border{Width=16,Height=16,CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(2),
                BorderBrush=Brushes.White,Background=active?new SolidColorBrush(Color.FromRgb(97,232,240)):new SolidColorBrush(Color.FromRgb(18,41,58))};
            point.Content=circle;point.Click+=(_,_)=>ButtonSelected?.Invoke(item.Slot);
            System.Windows.Automation.AutomationProperties.SetName(point,$"Punto G{item.Slot} · {MouseButtonLayout.Position(item.Slot)}");
            Canvas.SetLeft(point,item.X-13);Canvas.SetTop(point,item.Y-13);canvas.Children.Add(point);
        }
    }
}
