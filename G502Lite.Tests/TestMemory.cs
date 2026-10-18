using G502Lite.Device;

// Synthetic protocol fixture: no device dumps, serial numbers or user macros.
internal static class TestMemory
{
    public static MemorySnapshot Create()
    {
        var data=new byte[256];
        data[0]=1;data[1]=2;
        int[] dpi={1200,2400,3200,6400,0};
        for(int i=0;i<dpi.Length;i++){data[3+i*2]=(byte)dpi[i];data[4+i*2]=(byte)(dpi[i]>>8);}
        string[] buttons={"80010001","80010002","80010004","800300B5","800300CD","800300B6","90040000","90030000","900A0000","90020000","90010000"};
        for(int i=0;i<buttons.Length;i++)Convert.FromHexString(buttons[i]).CopyTo(data,32+i*4);
        for(int i=96;i<254;i++)data[i]=(byte)(i*17+3);
        LightingEffect.Cycle(8000).CopyTo(data,208);
        LightingEffect.Cycle(8000).CopyTo(data,219);
        var crc=OnboardMemory.Crc(data.AsSpan(0,254));data[254]=(byte)(crc>>8);data[255]=(byte)crc;
        return new MemorySnapshot("01020000000B100100",1,new Dictionary<int,string>{{1,Convert.ToHexString(data)}});
    }
}
