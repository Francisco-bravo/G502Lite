using System.Text.Json;

namespace G502Lite;

internal static class ClickSpeedSettings
{
    public const int Default=10,Minimum=1,Maximum=50;
    private static string SettingsPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"G502Lite","click-speed.json");
    public static TimeSpan Interval(int clicksPerSecond)
    {
        if(clicksPerSecond<Minimum||clicksPerSecond>Maximum)throw new ArgumentOutOfRangeException(nameof(clicksPerSecond),"Elige entre 1 y 50 clics por segundo.");
        return TimeSpan.FromMilliseconds(1000d/clicksPerSecond);
    }
    public static int Load(string? path=null)
    {
        try
        {
            var speed=JsonSerializer.Deserialize<int>(File.ReadAllText(path??SettingsPath));
            Interval(speed);return speed;
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException or ArgumentOutOfRangeException){return Default;}
    }
    public static void Save(int speed,string? path=null)
    {
        Interval(speed);
        var target=path??SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        var temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,JsonSerializer.Serialize(speed));File.Move(temporary,target,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
