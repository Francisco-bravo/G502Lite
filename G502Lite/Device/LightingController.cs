using System.Globalization;

namespace G502Lite.Device;

internal sealed class LightingController
{
    private readonly HidPlusTransport transport;
    private readonly byte feature;
    private readonly bool legacy;
    private readonly List<(byte Zone, byte Static, byte Off,byte? Cycle)> zones=new();
    public bool Available=>zones.Count>0;
    public int ZoneCount=>zones.Count;
    public bool SupportsCycle=>Available&&zones.All(x=>x.Cycle.HasValue);
    private LightingController(HidPlusTransport transport,byte feature,bool legacy){this.transport=transport;this.feature=feature;this.legacy=legacy;}
    public static async Task<LightingController?> DiscoverAsync(HidPlusTransport transport,byte feature,bool legacy,CancellationToken token)
    {
        if(feature==0)return null;
        var controller=new LightingController(transport,feature,legacy);
        var info=await transport.RequestAsync(feature,0,legacy?null:new byte[]{255,255,0},token);
        var count=info[legacy?4:6];
        if(count>16)throw new IOException("Cantidad de zonas RGB no válida.");
        for(byte zone=0;zone<count;zone++)
        {
            var detail=await transport.RequestAsync(feature,legacy?(byte)1:(byte)0,new byte[]{zone,255,0},token);
            var effects=detail[legacy?7:8];
            if(effects>32)throw new IOException("Cantidad de efectos RGB no válida.");
            byte? fixedIndex=null,offIndex=null,cycleIndex=null;
            for(byte effect=0;effect<effects;effect++)
            {
                var response=await transport.RequestAsync(feature,legacy?(byte)2:(byte)0,new byte[]{zone,effect,0},token);
                var id=(response[6]<<8)|response[7];
                if(id==0)offIndex=effect;
                if(id==1)fixedIndex=effect;
                if(id==3)cycleIndex=effect;
            }
            if(fixedIndex.HasValue&&offIndex.HasValue)controller.zones.Add((zone,fixedIndex.Value,offIndex.Value,cycleIndex));
        }
        return controller;
    }
    public async Task ApplyAsync(string hex,bool off,CancellationToken token)
    {
        await ApplyEffectAsync(LightingEffect.Fixed(hex,off),token);
    }
    public async Task ApplyEffectAsync(byte[] block,CancellationToken token)
    {
        if(!Available)throw new NotSupportedException("No hay zonas RGB compatibles.");
        var effect=LightingEffect.Decode(block)??throw new ArgumentException("Efecto inválido.");
        if(effect.Mode==LightingMode.Cycle&&!SupportsCycle)throw new NotSupportedException("El mouse no anuncia ciclo multicolor en todas las zonas.");
        // Apply only the requested effects. Do not switch onboard mode or take
        // permanent software ownership of lighting while discovering/connecting.
        foreach(var zone in zones)
        {
            var payload=new byte[13];payload[0]=zone.Zone;
            payload[1]=effect.Mode switch{LightingMode.Off=>zone.Off,LightingMode.Fixed=>zone.Static,LightingMode.Cycle=>zone.Cycle!.Value,_=>throw new NotSupportedException()};
            block.AsSpan(1,10).CopyTo(payload.AsSpan(2,10));payload[12]=legacy?(byte)0:(byte)1;
            await transport.RequestAsync(feature,legacy?(byte)3:(byte)1,payload,token);
        }
    }
}
