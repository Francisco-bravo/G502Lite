using G502Lite.Device;

namespace G502Lite;

// Driven on the input dispatcher: no queued clicks survive a release or Stop().
internal sealed class HoldClick
{
    public const string Binding="80020072"; // F23, reserved for the G502 only.
    public const string Name="Clic continuo mientras mantienes pulsado";
    public const int IntervalMilliseconds=100;
    private readonly Action click;
    public bool Active { get; private set; }
    public HoldClick(Action click)=>this.click=click;
    public void Read(ButtonActivity value,int[] slots)
    {
        if(value.Binding!=Binding)return;
        if(value.Pressed==false){Stop();return;}
        if(value.Pressed!=true||Active||!slots.Any(x=>x is >=2 and <=11))return;
        Active=true;
        Tick();
    }
    public void Tick()
    {
        if(!Active)return;
        try{click();}
        catch{Stop();throw;}
    }
    public void Stop()=>Active=false;
}
