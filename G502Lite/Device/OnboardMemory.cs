namespace G502Lite.Device;

public sealed record MemorySnapshot(string Descriptor,int ActiveSector,Dictionary<int,string> Sectors)
{
    public byte[] GetSector(int sector)=>Convert.FromHexString(Sectors[sector]);
}
public sealed record ButtonState(int Slot,string Action,string Hex);
public sealed record OnboardSettings(int Sector,bool CrcValid,int[] DpiLevels,int DefaultIndex,int ShiftIndex,IReadOnlyList<ButtonState> Buttons);

internal static class OnboardMemory
{
    public static byte[] PatchButtons(MemorySnapshot snapshot,IReadOnlyDictionary<int,string> bindings)
    {
        var settings=Decode(snapshot);
        if(!settings.CrcValid)throw new IOException("La memoria no supera la comprobación CRC. No se modificó.");
        var data=snapshot.GetSector(snapshot.ActiveSector);
        foreach(var entry in bindings)
        {
            if(entry.Key<2||entry.Key>11)throw new ArgumentException("El clic izquierdo se conserva.");
            var binding=Convert.FromHexString(entry.Value);
            if(binding.Length!=4||binding[0] is not (0x80 or 0x90))throw new ArgumentException("Asignación no admitida para escritura.");
            binding.CopyTo(data,32+(entry.Key-1)*4);
        }
        var crc=Crc(data.AsSpan(0,254));data[254]=(byte)(crc>>8);data[255]=(byte)crc;
        return data;
    }
    public static async Task WriteActiveAsync(HidPlusTransport transport,byte feature,MemorySnapshot expected,byte[] replacement,string backupPath,CancellationToken token,bool restore=false)
    {
        var settings=Decode(expected);
        if((!settings.CrcValid&&!restore)||replacement.Length!=256||Crc(replacement.AsSpan(0,254))!=((replacement[254]<<8)|replacement[255]))throw new IOException("La configuración no tiene un CRC válido.");
        var mode=await transport.RequestAsync(feature,2,null,token);
        if(mode[4]!=1)throw new IOException("Activa la memoria integrada del mouse antes de guardar botones.");
        var fresh=await ReadAsync(transport,feature,token);
        if(fresh.Descriptor!=expected.Descriptor||fresh.ActiveSector!=expected.ActiveSector||fresh.Sectors.Any(x=>expected.Sectors.GetValueOrDefault(x.Key)!=x.Value))throw new IOException("La memoria cambió desde su lectura. Vuelve a abrir Botones para actualizarla.");
        if(replacement.AsSpan().SequenceEqual(fresh.GetSector(fresh.ActiveSector)))return;
        var directory=Path.GetDirectoryName(backupPath)!;Directory.CreateDirectory(directory);
        // Make a complete, durable backup before the first flash command.
        using(var file=new FileStream(backupPath,FileMode.CreateNew,FileAccess.Write,FileShare.Read))
        {
            System.Text.Json.JsonSerializer.Serialize(file,expected,new System.Text.Json.JsonSerializerOptions{WriteIndented=true});file.Flush(true);
        }
        token.ThrowIfCancellationRequested();
        // Once a flash transfer begins, closing the window waits for completion.
        // No cancellation or replay of a partially acknowledged transfer.
        var sector=fresh.ActiveSector;
        try
        {
            await transport.RequestAsync(feature,6,new[]{(byte)(sector>>8),(byte)sector,(byte)0,(byte)0,(byte)1,(byte)0});
            for(int offset=0;offset<256;offset+=16)await transport.RequestAsync(feature,7,replacement.AsSpan(offset,16).ToArray());
            await transport.RequestAsync(feature,8);
            var check=await ReadAsync(transport,feature,CancellationToken.None);
            if(!check.GetSector(sector).AsSpan().SequenceEqual(replacement))throw new IOException("La lectura posterior no coincide con la escritura.");
            foreach(var entry in fresh.Sectors.Where(x=>x.Key!=sector))if(check.Sectors[entry.Key]!=entry.Value)throw new IOException("Cambió un sector ajeno a la configuración activa.");
            // Reload precisely the same active memory; no profile switching.
            await transport.RequestAsync(feature,3,new[]{(byte)(sector>>8),(byte)sector});
        }
        catch(Exception ex){throw new IOException($"No se pudo verificar el guardado. Copia previa: {backupPath}. {ex.Message}",ex);}
    }
    public static ushort Crc(ReadOnlySpan<byte> bytes)
    {
        ushort value=0xffff;
        foreach(var b in bytes)
        {
            value^=(ushort)(b<<8);
            for(int bit=0;bit<8;bit++)value=(ushort)((value<<1)^((value&0x8000)!=0?0x1021:0));
        }
        return value;
    }
    public static OnboardSettings Decode(MemorySnapshot snapshot)
    {
        var descriptor=Convert.FromHexString(snapshot.Descriptor);
        if(descriptor[0]!=1||descriptor[1]!=2||descriptor[5]!=11)throw new NotSupportedException("Formato de memoria no validado para este modelo.");
        var data=snapshot.GetSector(snapshot.ActiveSector);
        if(data.Length!=256)throw new NotSupportedException("Tamaño de memoria no compatible.");
        var valid=Crc(data.AsSpan(0,254))==((data[254]<<8)|data[255]);
        var levels=Enumerable.Range(0,5).Select(i=>(int)data[3+i*2]|(data[4+i*2]<<8)).ToArray();
        var buttons=Enumerable.Range(0,11).Select(i=>
        {
            var bytes=data.AsSpan(32+i*4,4).ToArray();
            return new ButtonState(i+1,Describe(bytes),Convert.ToHexString(bytes));
        }).ToArray();
        return new(snapshot.ActiveSector,valid,levels,data[1],data[2],buttons);
    }
    public static string Describe(byte[] b)
    {
        if(Convert.ToHexString(b)==G502Lite.TripleClick.Binding)return G502Lite.TripleClick.Name+" · requiere G502 Lite";
        if(Convert.ToHexString(b)==G502Lite.HoldClick.Binding)return G502Lite.HoldClick.Name+" · requiere G502 Lite";
        if(b[0]==0x80)
        {
            var value=(b[2]<<8)|b[3];
            if(b[1]==0)return "Sin acción";
            if(b[1]==1)return value switch{1=>"Clic izquierdo",2=>"Clic derecho",4=>"Clic central",8=>"Atrás",16=>"Adelante",_=>$"Botón HID {value}"};
            if(b[1]==2)return $"Tecla HID {b[3]:X2} · Modificadores {b[2]:X2}";
            if(b[1]==3)return value switch{0xb5=>"Pista siguiente",0xb6=>"Pista anterior",0xcd=>"Reproducir / pausa",0xe2=>"Silenciar",0xe9=>"Subir volumen",0xea=>"Bajar volumen",_=>$"Multimedia {value:X4}"};
        }
        if(b[0]==0x90)return b[1] switch{1=>"Rueda izquierda",2=>"Rueda derecha",3=>"Subir DPI",4=>"Bajar DPI",5=>"Alternar DPI",6=>"DPI predeterminado",7=>"DPI Shift",8=>"Siguiente memoria",9=>"Memoria anterior",10=>"Alternar memoria",11=>"G-Shift",_=>$"Función {b[1]:X2}"};
        if((b[0]>>4)<=2)return "Macro integrada (se conserva)";
        return $"Asignación {Convert.ToHexString(b)}";
    }
    public static async Task<MemorySnapshot> ReadAsync(HidPlusTransport transport,byte feature,CancellationToken token)
    {
        var descriptor=(await transport.RequestAsync(feature,0,null,token))[4..];
        var count=descriptor[6];var size=(descriptor[7]<<8)|descriptor[8];
        if(descriptor[0]!=1||count==0||count>32||size<16||size>4096)throw new NotSupportedException("Descriptor de memoria fuera del formato admitido.");
        var active=await transport.RequestAsync(feature,4,null,token);
        var activeSector=(active[4]<<8)|active[5];
        var sectors=new Dictionary<int,string>();
        for(int sector=0;sector<count;sector++)
        {
            var data=new byte[size];
            for(int offset=0;offset<size;offset+=16)
            {
                var position=Math.Min(offset,size-16);
                var response=await transport.RequestAsync(feature,5,new[]{(byte)(sector>>8),(byte)sector,(byte)(position>>8),(byte)position},token);
                response.AsSpan(4,16).CopyTo(data.AsSpan(position,16));
            }
            sectors.Add(sector,Convert.ToHexString(data));
        }
        if(!sectors.ContainsKey(activeSector))throw new NotSupportedException("La memoria activa no está en los sectores de usuario.");
        return new(Convert.ToHexString(descriptor),activeSector,sectors);
    }
}
