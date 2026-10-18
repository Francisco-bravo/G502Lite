using System.Text.Json;
using G502Lite.Device;

namespace G502Lite;
internal static class DiagnosticCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if(args.Length!=2||args[0] is not ("--diagnose" or "--backup" or "--self-test" or "--test-buttons" or "--test-lighting" or "--test-cycle"))return 2;
        var output=Path.GetFullPath(args[1]);
        try
        {
            var endpoints=NativeHid.EnumerateG502();
            await using var device=await DeviceController.ConnectAsync();
            if(args[0]=="--backup")
            {
                var backup=await device.BackupMemoryAsync();
                await File.WriteAllTextAsync(output,JsonSerializer.Serialize(backup,new JsonSerializerOptions{WriteIndented=true}));return 0;
            }
            if(args[0] is "--self-test" or "--test-buttons" or "--test-lighting" or "--test-cycle")return await TestAsync(device,args[0],output);
            var state=await device.RefreshAsync();
            var onboard=await device.ReadOnboardDescriptorAsync();
            await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new {ok=true,state,onboard,interfaces=endpoints.Select(x=>new{x.UsagePage,x.Usage,x.InputLength,x.OutputLength})},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception e){await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{ok=false,error=e.ToString()},new JsonSerializerOptions{WriteIndented=true}));return 1;}
    }
    private static async Task<int> TestAsync(DeviceController device,string operation,string output)
    {
        var initial=await device.RefreshAsync();var steps=new List<object>();
        MemorySnapshot? backup=null;string? failure=null;bool restored=false;
        try
        {
            if(operation=="--test-cycle")
            {
                if(initial.OnboardMode!="Memoria integrada"||!initial.SupportsColorCycle)throw new NotSupportedException("La prueba requiere ciclo nativo y memoria integrada.");
                backup=await device.BackupMemoryAsync();
                foreach(var period in new[]{6000,12500})
                {
                    var setting=await device.SetColorCycleAsync(period);
                    var changed=await device.BackupMemoryAsync();var data=changed.GetSector(changed.ActiveSector);
                    var expected=LightingEffect.Cycle(period);
                    for(int zone=0;zone<2;zone++)if(!data.AsSpan(208+zone*11,11).SequenceEqual(expected))throw new IOException("El bloque multicolor guardado no coincide.");
                    if(setting.Lighting?.Mode!=LightingMode.Cycle||setting.Lighting.CycleMilliseconds!=period)throw new IOException("La lectura del ciclo no coincide.");
                    steps.Add(new{cycleMilliseconds=period,zones=2,verifiedInMemory=true,readback=setting.Lighting});
                }
            }
            else if(operation=="--test-lighting")
            {
                if(initial.OnboardMode!="Memoria integrada")throw new NotSupportedException("La prueba RGB requiere copia restaurable de la memoria integrada.");
                backup=await device.BackupMemoryAsync();
                await device.SetLightingAsync("#61E8F0",false);
                var changed=await device.BackupMemoryAsync();var data=changed.GetSector(changed.ActiveSector);
                for(int zone=0;zone<2;zone++)if(Convert.ToHexString(data.AsSpan(208+zone*11,4))!="0161E8F0")throw new IOException("La memoria no confirma el color de prueba.");
                steps.Add(new{rgb="#61E8F0",zones=2,verifiedInMemory=true});
                await device.SetLightingAsync("#000000",true);
                changed=await device.BackupMemoryAsync();data=changed.GetSector(changed.ActiveSector);
                for(int zone=0;zone<2;zone++)if(data[208+zone*11]!=0)throw new IOException("La memoria no confirma el apagado.");
                steps.Add(new{lightsOff=true,verifiedInMemory=true});
            }
            else if(operation=="--test-buttons")
            {
                backup=await device.BackupMemoryAsync();
                var changed=await device.SaveButtonsAsync(backup,new Dictionary<int,string>{{9,"80020106"}});
                var setting=OnboardMemory.Decode(changed);
                if(setting.Buttons[8].Hex!="80020106"||!setting.CrcValid)throw new IOException("Falló la lectura del botón de prueba.");
                steps.Add(new{button=9,assignment="Ctrl+C",crcValid=setting.CrcValid,verified=true});
            }
            else
            {
                var target=initial.Dpi==2400?3200:2400;
                var dpi=await device.SetDpiAsync(target);steps.Add(new{dpi=dpi.Dpi,verified=dpi.Dpi==target});
                var targetHz=initial.ReportRateHz==500?1000:500;
                var rate=await device.SetReportRateAsync(targetHz);steps.Add(new{hz=rate.ReportRateHz,verified=rate.ReportRateHz==targetHz});
            }
        }
        catch(Exception e){failure=e.ToString();}
        finally
        {
            try
            {
                if(backup is not null)
                {
                    var current=await device.BackupMemoryAsync();
                    var recovered=await device.RestoreActiveMemoryAsync(current,backup);
                    if(recovered.Sectors.Any(x=>backup.Sectors[x.Key]!=x.Value))throw new IOException("La copia restaurada difiere del original.");
                }
                await device.SetDpiAsync(initial.Dpi);
                if(initial.ReportRateHz is int hz)await device.SetReportRateAsync(hz);
                var final=await device.RefreshAsync();restored=final.Dpi==initial.Dpi&&final.ReportRateHz==initial.ReportRateHz;
            }
            catch(Exception e){failure=(failure??"")+"\nRestauración: "+e;}
        }
        await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{ok=failure is null&&restored,operation,initial,steps,restored,error=failure},new JsonSerializerOptions{WriteIndented=true}));
        return failure is null&&restored?0:1;
    }
}
