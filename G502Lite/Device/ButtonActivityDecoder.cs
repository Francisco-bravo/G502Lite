namespace G502Lite.Device;

internal sealed record ButtonActivity(string Binding,string Name,bool? Pressed);

internal static class ButtonActivityDecoder
{
    // G502 HERO Consumer Control report, observed on 046D:C08B (report 03).
    public static IReadOnlySet<ushort>? ConsumerUsages(ReadOnlySpan<byte> report)
    {
        if(report.Length!=5||report[0]!=3)return null;
        var result=new HashSet<ushort>();
        for(int p=1;p<5;p+=2){var usage=(ushort)(report[p]|(report[p+1]<<8));if(usage!=0)result.Add(usage);}
        return result;
    }
    public static ButtonActivity Consumer(ushort usage,bool pressed)
    {
        var hex=$"8003{usage:X4}";
        return new(hex,OnboardMemory.Describe(Convert.FromHexString(hex)),pressed);
    }
    public static IEnumerable<ButtonActivity> Mouse(ushort flags,short delta)
    {
        (ushort Down,ushort Up,string Hex,string Name)[] buttons=
        {
            (1,2,"80010001","Clic izquierdo"),(4,8,"80010002","Clic derecho"),
            (16,32,"80010004","Clic central"),(64,128,"80010008","Atrás"),(256,512,"80010010","Adelante")
        };
        foreach(var b in buttons)
        {
            if((flags&b.Down)!=0)yield return new(b.Hex,b.Name,true);
            if((flags&b.Up)!=0)yield return new(b.Hex,b.Name,false);
        }
        if((flags&0x400)!=0&&delta!=0)yield return new("",delta>0?"Rueda arriba":"Rueda abajo",null);
        if((flags&0x800)!=0&&delta!=0)yield return new(delta>0?"90020000":"90010000",delta>0?"Rueda derecha":"Rueda izquierda",null);
    }
    public static int[] MatchingSlots(MemorySnapshot? snapshot,string binding)
    {
        if(snapshot is null||binding.Length!=8||!OnboardMemory.Decode(snapshot).CrcValid)return Array.Empty<int>();
        return OnboardMemory.Decode(snapshot).Buttons.Where(x=>binding.StartsWith("90")?x.Hex.StartsWith(binding[..4]):x.Hex==binding).Select(x=>x.Slot).ToArray();
    }
}

internal sealed class G502KeyboardActivity
{
    private byte modifiers;
    private readonly Dictionary<ushort,ButtonActivity> held=new();
    public ButtonActivity? Read(ushort key,ushort scan,ushort flags)
    {
        bool released=(flags&1)!=0,extended=(flags&2)!=0;
        byte bit=key switch
        {
            0x11=>(byte)(extended?16:1),0xa2=>1,0xa3=>16,
            0x10=>(byte)(scan==0x36?32:2),0xa0=>2,0xa1=>32,
            0x12=>(byte)(extended?64:4),0xa4=>4,0xa5=>64,0x5b=>8,0x5c=>128,_=>0
        };
        if(bit!=0){if(released)modifiers&=(byte)~bit;else modifiers|=bit;return null;}
        ushort id=(ushort)(scan|(extended?0x8000:0));
        if(released){if(held.Remove(id,out var old))return old with{Pressed=false};return null;}
        if(held.ContainsKey(id))return null;
        int usage=key switch
        {
            >=0x41 and <=0x5a=>key-0x41+4,>=0x70 and <=0x7b=>key-0x70+0x3a,>=0x7c and <=0x87=>key-0x7c+0x68,
            >=0x31 and <=0x39=>key-0x31+0x1e,0x30=>0x27,
            0x0d=>0x28,0x1b=>0x29,0x08=>0x2a,0x09=>0x2b,0x20=>0x2c,_=>0
        };
        if(usage==0)return null;
        string name=key switch{>=0x41 and <=0x5a=>((char)key).ToString(),>=0x70 and <=0x87=>$"F{key-0x70+1}",>=0x30 and <=0x39=>((char)key).ToString(),0x0d=>"Enter",0x1b=>"Escape",0x08=>"Retroceso",0x09=>"Tabulación",_=>"Espacio"};
        string prefix=((modifiers&0x11)!=0?"Ctrl+":"")+((modifiers&0x22)!=0?"Shift+":"")+((modifiers&0x44)!=0?"Alt+":"")+((modifiers&0x88)!=0?"Win+":"");
        var value=new ButtonActivity($"8002{modifiers:X2}{usage:X2}",prefix+name,true);held[id]=value;return value;
    }
}
