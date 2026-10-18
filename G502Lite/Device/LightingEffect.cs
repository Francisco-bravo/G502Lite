using System.Globalization;

namespace G502Lite.Device;

public enum LightingMode { Off, Fixed, Cycle }

public sealed record LightingSetting(LightingMode Mode,string? ColorHex,int? CycleMilliseconds);

internal static class LightingEffect
{
    public const int MinimumCycleMilliseconds=1000;
    public const int MaximumCycleMilliseconds=60000;

    public static int ParseCycleMilliseconds(string seconds)
    {
        if(!decimal.TryParse(seconds.Trim().Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value)||value<1||value>60)
            throw new ArgumentException("La duración del ciclo debe estar entre 1 y 60 segundos; puedes usar decimales.");
        return decimal.ToInt32(decimal.Round(value*1000,0,MidpointRounding.AwayFromZero));
    }
    public static byte[] Fixed(string hex,bool off)
    {
        hex=hex.TrimStart('#');
        if(hex.Length!=6||!uint.TryParse(hex,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var color))throw new ArgumentException("Color hexadecimal inválido.");
        var block=new byte[11];
        if(!off){block[0]=1;block[1]=(byte)(color>>16);block[2]=(byte)(color>>8);block[3]=(byte)color;}
        return block;
    }
    public static byte[] Cycle(int milliseconds)
    {
        if(milliseconds<MinimumCycleMilliseconds||milliseconds>MaximumCycleMilliseconds)throw new ArgumentOutOfRangeException(nameof(milliseconds),"El ciclo debe durar entre 1 y 60 segundos.");
        // Effect 0003: five reserved parameter bytes, big-endian period in
        // milliseconds, intensity percent, then two reserved bytes.
        var block=new byte[11];block[0]=3;block[6]=(byte)(milliseconds>>8);block[7]=(byte)milliseconds;block[8]=100;
        return block;
    }
    public static LightingSetting? Decode(ReadOnlySpan<byte> block)
    {
        if(block.Length!=11)return null;
        return block[0] switch
        {
            0=>new(LightingMode.Off,null,null),
            1=>new(LightingMode.Fixed,$"#{block[1]:X2}{block[2]:X2}{block[3]:X2}",null),
            3=>new(LightingMode.Cycle,null,(block[6]<<8)|block[7]),
            _=>null
        };
    }
    public static byte[] PatchOnboard(MemorySnapshot snapshot,byte[] block)
    {
        var settings=OnboardMemory.Decode(snapshot);
        if(!settings.CrcValid)throw new IOException("La memoria no supera la comprobación CRC. No se modificó.");
        if(Decode(block) is null)throw new ArgumentException("Efecto de iluminación no compatible.");
        var data=snapshot.GetSector(snapshot.ActiveSector);
        block.CopyTo(data,208);block.CopyTo(data,219);
        var crc=OnboardMemory.Crc(data.AsSpan(0,254));data[254]=(byte)(crc>>8);data[255]=(byte)crc;
        return data;
    }
}
