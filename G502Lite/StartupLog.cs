namespace G502Lite;

internal static class StartupLog
{
    public static void Write(string message)
    {
        try
        {
            var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"G502Lite");
            Directory.CreateDirectory(folder);
            var path=Path.Combine(folder,"startup.log");
            if(File.Exists(path)&&new FileInfo(path).Length>65536)File.Move(path,path+".old",true);
            File.AppendAllText(path,$"{DateTimeOffset.Now:O} PID={Environment.ProcessId} {message}{Environment.NewLine}");
        }
        catch { /* A diagnostic log must never prevent application startup. */ }
    }
}
