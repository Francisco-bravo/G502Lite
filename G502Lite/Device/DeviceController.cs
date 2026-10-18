using System.Diagnostics;

namespace G502Lite.Device;

public sealed record DeviceState
{
    public string ModelName { get; init; } = "G502 HERO K/DA";
    public int Dpi { get; init; }
    public int DefaultDpi { get; init; }
    public int MinimumDpi { get; init; }
    public int MaximumDpi { get; init; }
    public int DpiStep { get; init; }
    public IReadOnlyList<int> DpiValues { get; init; } = Array.Empty<int>();
    public int? ReportRateHz { get; init; }
    public IReadOnlyList<int> SupportedReportRatesHz { get; init; } = Array.Empty<int>();
    public bool SupportsLighting { get; init; }
    public bool SupportsColorCycle { get; init; }
    public LightingSetting? Lighting { get; init; }
    public bool GHubRunning { get; init; }
    public string? OnboardMode { get; init; }
}

public sealed class DeviceController : IAsyncDisposable
{
    private readonly HidPlusTransport transport;
    private readonly SemaphoreSlim operations=new(1,1);
    private readonly Dictionary<ushort,(byte Index,byte Version)> features=new();
    private readonly List<(int Min,int Max,int Step)> ranges=new();
    private readonly List<int> dpiValues=new();
    private byte dpiFeature, dpiVersion;
    private byte? rateFeature,onboardFeature;
    private readonly List<int> reportRates=new();
    private DeviceState? state;
    private DeviceController(HidPlusTransport transport) { this.transport=transport; }

    public static async Task<DeviceController> ConnectAsync(CancellationToken token=default)
    {
        var endpoints=await Task.Run(NativeHid.EnumerateG502,token);
        var endpoint=endpoints.FirstOrDefault(x=>x.UsagePage==0xff00&&x.Usage==2&&x.InputLength==20&&x.OutputLength==20)
            ?? endpoints.FirstOrDefault(x=>x.UsagePage>=0xff00&&x.InputLength==20&&x.OutputLength==20);
        if(endpoint is null)throw new IOException("Conecta el G502 HERO por USB. No se encontró su interfaz HID++ de 20 bytes.");
        var controller=new DeviceController(new HidPlusTransport(endpoint));
        try { await controller.InitializeAsync(token);return controller; }
        catch { await controller.DisposeAsync();throw; }
    }
    private async Task<(byte Index,byte Version)> FeatureAsync(ushort id,CancellationToken token)
    {
        if(features.TryGetValue(id,out var known))return known;
        var r=await transport.RequestAsync(0,0,new[]{(byte)(id>>8),(byte)id},token);
        if(r.Length<7)throw new IOException("Respuesta de descubrimiento incompleta.");
        var feature=(Index:r[4],Version:r[6]);features[id]=feature;return feature;
    }
    private async Task InitializeAsync(CancellationToken token)
    {
        var dpi=await FeatureAsync(0x2201,token);if(dpi.Index==0)throw new IOException("Este dispositivo no anuncia la función de DPI compatible.");
        dpiFeature=dpi.Index;dpiVersion=dpi.Version;
        var list=await transport.RequestAsync(dpiFeature,1,new byte[]{0},token);
        ParseDpiList(list);
        if(ranges.Count==0&&dpiValues.Count==0)throw new IOException("El mouse no devolvió un rango de DPI válido.");
        var rate=await FeatureAsync(0x8060,token);
        if(rate.Index!=0)
        {
            rateFeature=rate.Index;var caps=await transport.RequestAsync(rate.Index,0,null,token);
            for(var bit=0;bit<8;bit++)if((caps[4]&(1<<bit))!=0)reportRates.Add(1000/(bit+1));
        }
        var onboard=await FeatureAsync(0x8100,token);if(onboard.Index!=0)onboardFeature=onboard.Index;
        await InitializeLightingAsync(token);
        await RefreshAsync(token);
    }
    private void ParseDpiList(byte[] report)
    {
        var values=new List<int>();for(int p=5;p+1<report.Length;p+=2){var value=(report[p]<<8)|report[p+1];if(value==0)break;values.Add(value);}
        for(int i=0;i<values.Count;i++)
        {
            var value=values[i];
            if(value>0xe000)
            {
                if(i==0||i+1>=values.Count||values[i-1]>0xe000||values[i+1]>0xe000)throw new IOException("Rango de DPI mal formado.");
                var step=value-0xe000;if(step<=0||values[i+1]<values[i-1])throw new IOException("Paso de DPI inválido.");
                ranges.Add((values[i-1],values[i+1],step));
            }
            else dpiValues.Add(value);
        }
    }
    public async Task<DeviceState> RefreshAsync(CancellationToken token=default)
    {
        await operations.WaitAsync(token);
        try { return await RefreshCoreAsync(token); }
        finally{operations.Release();}
    }
    private async Task<DeviceState> RefreshCoreAsync(CancellationToken token)
    {
        var dpi=await transport.RequestAsync(dpiFeature,2,new byte[]{0},token);
        if(dpi.Length<9||dpi[4]!=0)throw new IOException("Respuesta de DPI inválida.");
        int? hz=null;if(rateFeature is byte rate){var r=await transport.RequestAsync(rate,1,null,token);if(r[4]>0)hz=1000/r[4];}
        string? mode=null;if(onboardFeature is byte onboard){var r=await transport.RequestAsync(onboard,2,null,token);mode=r[4] switch {1=>"Memoria integrada",2=>"Control por software",_=>"Modo desconocido"};}
        LightingSetting? currentLighting=null;
        if(mode=="Memoria integrada"&&lightingReady&&onboardFeature is byte memory)
            currentLighting=await ReadOnboardLightingAsync(memory,token);
        state=new DeviceState {Dpi=(dpi[5]<<8)|dpi[6],DefaultDpi=(dpi[7]<<8)|dpi[8],MinimumDpi=dpiValues.Min(),MaximumDpi=dpiValues.Max(),DpiStep=ranges.Count==1?ranges[0].Step:1,DpiValues=dpiValues.Distinct().ToArray(),ReportRateHz=hz,SupportedReportRatesHz=reportRates.ToArray(),SupportsLighting=lightingReady,SupportsColorCycle=lighting?.SupportsCycle==true,Lighting=currentLighting,GHubRunning=IsGHubRunning(),OnboardMode=mode};
        return state;
    }
    private static bool IsGHubRunning()
    {
        foreach(var process in Process.GetProcessesByName("lghub_agent")){process.Dispose();return true;}return false;
    }
    public async Task<DeviceState> SetDpiAsync(int dpi,CancellationToken token=default)
    {
        if(dpiVersion>1)throw new IOException("La versión de la función DPI no está verificada para escritura.");
        if(!dpiValues.Contains(dpi)&&!ranges.Any(r=>dpi>=r.Min&&dpi<=r.Max&&(dpi-r.Min)%r.Step==0))throw new ArgumentOutOfRangeException(nameof(dpi),"El valor no pertenece a los pasos de DPI que anuncia el mouse.");
        await operations.WaitAsync(token);
        try
        {
            await transport.RequestAsync(dpiFeature,3,new[]{(byte)0,(byte)(dpi>>8),(byte)dpi},token);
            var current=await RefreshCoreAsync(token);if(current.Dpi!=dpi)throw new IOException($"El dispositivo informa {current.Dpi} DPI después del cambio. Otra aplicación puede estar aplicando ajustes.");return current;
        }
        finally{operations.Release();}
    }
    public async Task<DeviceState> SetReportRateAsync(int hz,CancellationToken token=default)
    {
        if(rateFeature is not byte feature||!reportRates.Contains(hz))throw new ArgumentOutOfRangeException(nameof(hz),"Frecuencia no admitida por el dispositivo.");
        await operations.WaitAsync(token);
        try
        {
            var ms=Enumerable.Range(1,8).First(x=>1000/x==hz);
            var before=await RefreshCoreAsync(token);
            if(before.OnboardMode=="Memoria integrada"&&onboardFeature is byte onboard)
            {
                var backup=await OnboardMemory.ReadAsync(transport,onboard,token);
                OnboardMemory.Decode(backup);
                var replacement=backup.GetSector(backup.ActiveSector);replacement[0]=(byte)ms;
                UpdateCrc(replacement);
                await SaveMemoryCoreAsync(backup,replacement,token);
            }
            else await transport.RequestAsync(feature,2,new[]{(byte)ms},token);
            var current=await RefreshCoreAsync(token);if(current.ReportRateHz!=hz)throw new IOException("La frecuencia leída no coincide con la solicitada.");return current;
        }
        finally{operations.Release();}
    }
    private LightingController? lighting;
    private bool lightingReady=>lighting?.Available==true;
    private async Task InitializeLightingAsync(CancellationToken token)
    {
        var rgb=await FeatureAsync(0x8071,token);
        if(rgb.Index!=0)lighting=await LightingController.DiscoverAsync(transport,rgb.Index,false,token);
        else
        {
            var led=await FeatureAsync(0x8070,token);
            lighting=await LightingController.DiscoverAsync(transport,led.Index,true,token);
        }
    }
    public async Task<DeviceState> SetLightingAsync(string hexColor,bool off,CancellationToken token=default)
    {
        return await SetLightingEffectAsync(LightingEffect.Fixed(hexColor,off),token);
    }
    public async Task<DeviceState> SetColorCycleAsync(int milliseconds,CancellationToken token=default)
    {
        if(lighting?.SupportsCycle!=true)throw new NotSupportedException("El mouse no anuncia el efecto multicolor gradual.");
        return await SetLightingEffectAsync(LightingEffect.Cycle(milliseconds),token);
    }
    private async Task<DeviceState> SetLightingEffectAsync(byte[] block,CancellationToken token)
    {
        if(lighting is null)throw new NotSupportedException("Iluminación no compatible.");
        await operations.WaitAsync(token);
        try
        {
            var before=await RefreshCoreAsync(token);
            if(before.OnboardMode=="Memoria integrada"&&onboardFeature is byte onboard)
            {
                if(lighting.ZoneCount!=2)throw new NotSupportedException("La disposición de luces no coincide con la memoria validada del G502.");
                var backup=await OnboardMemory.ReadAsync(transport,onboard,token);
                var data=LightingEffect.PatchOnboard(backup,block);
                await SaveMemoryCoreAsync(backup,data,token);
            }
            else await lighting.ApplyEffectAsync(block,token);
            return await RefreshCoreAsync(token);
        }
        finally{operations.Release();}
    }
    private async Task<LightingSetting?> ReadOnboardLightingAsync(byte feature,CancellationToken token)
    {
        var descriptor=await transport.RequestAsync(feature,0,null,token);
        if(descriptor[4]!=1||descriptor[5]!=2||descriptor[9]!=11||descriptor[11]!=1||descriptor[12]!=0)return null;
        var profile=await transport.RequestAsync(feature,4,null,token);var sector=(profile[4]<<8)|profile[5];
        var data=new byte[256];
        for(int offset=0;offset<256;offset+=16)
        {
            var response=await transport.RequestAsync(feature,5,new[]{(byte)(sector>>8),(byte)sector,(byte)0,(byte)offset},token);
            response.AsSpan(4,16).CopyTo(data.AsSpan(offset,16));
        }
        if(OnboardMemory.Crc(data.AsSpan(0,254))!=((data[254]<<8)|data[255]))return null;
        var first=LightingEffect.Decode(data.AsSpan(208,11));var second=LightingEffect.Decode(data.AsSpan(219,11));
        return first==second?first:null;
    }
    public async Task<object> ReadOnboardDescriptorAsync(CancellationToken token=default)
    {
        if(onboardFeature is not byte feature)return new{available=false};
        await operations.WaitAsync(token);
        try
        {
            var descriptor=await transport.RequestAsync(feature,0,null,token);
            var profile=await transport.RequestAsync(feature,4,null,token);
            return new{available=true,descriptor=Convert.ToHexString(descriptor.AsSpan(4)),currentProfile=Convert.ToHexString(profile.AsSpan(4))};
        }
        finally{operations.Release();}
    }
    public ValueTask DisposeAsync()=>transport.DisposeAsync();
    public async Task<MemorySnapshot> BackupMemoryAsync(CancellationToken token=default)
    {
        if(onboardFeature is not byte feature)throw new NotSupportedException("No hay memoria integrada compatible.");
        await operations.WaitAsync(token);
        try{return await OnboardMemory.ReadAsync(transport,feature,token);}
        finally{operations.Release();}
    }
    public async Task<MemorySnapshot> SaveButtonsAsync(MemorySnapshot expected,IReadOnlyDictionary<int,string> bindings,CancellationToken token=default)
    {
        if(onboardFeature is not byte feature)throw new NotSupportedException("No hay memoria integrada compatible.");
        if(IsGHubRunning())throw new IOException("Cierra G HUB antes de guardar la memoria del mouse.");
        var replacement=OnboardMemory.PatchButtons(expected,bindings);
        return await SaveMemoryAsync(expected,replacement,token);
    }
    public async Task<MemorySnapshot> RestoreActiveMemoryAsync(MemorySnapshot expected,MemorySnapshot backup,CancellationToken token=default)
    {
        if(backup.Descriptor!=expected.Descriptor||backup.ActiveSector!=expected.ActiveSector)throw new ArgumentException("La copia no corresponde a la configuración activa.");
        OnboardMemory.Decode(backup);
        return await SaveMemoryAsync(expected,backup.GetSector(backup.ActiveSector),token,true);
    }
    private async Task<MemorySnapshot> SaveMemoryAsync(MemorySnapshot expected,byte[] replacement,CancellationToken token,bool restore=false)
    {
        await operations.WaitAsync(token);
        try{return await SaveMemoryCoreAsync(expected,replacement,token,restore);}
        finally{operations.Release();}
    }
    private static void UpdateCrc(byte[] data)
    {
        var crc=OnboardMemory.Crc(data.AsSpan(0,data.Length-2));data[^2]=(byte)(crc>>8);data[^1]=(byte)crc;
    }
    private async Task<MemorySnapshot> SaveMemoryCoreAsync(MemorySnapshot expected,byte[] replacement,CancellationToken token,bool restore=false)
    {
        if(onboardFeature is not byte feature)throw new NotSupportedException();
        if(IsGHubRunning())throw new IOException("Cierra G HUB antes de guardar la memoria del mouse.");
        var before=await RefreshCoreAsync(token);
        var backupPath=Path.Combine(AppContext.BaseDirectory,"backups",$"mouse-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        await OnboardMemory.WriteActiveAsync(transport,feature,expected,replacement,backupPath,token,restore);
        // Reloading the memory can reset live sensitivity. Preserve it.
        await transport.RequestAsync(dpiFeature,3,new[]{(byte)0,(byte)(before.Dpi>>8),(byte)before.Dpi});
        if(before.OnboardMode!="Memoria integrada"&&rateFeature is byte rate&&before.ReportRateHz is int hz)await transport.RequestAsync(rate,2,new[]{(byte)(1000/hz)});
        return await OnboardMemory.ReadAsync(transport,feature,CancellationToken.None);
    }
}
