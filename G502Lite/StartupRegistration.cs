using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace G502Lite;

internal static class StartupRegistration
{
    public const string RegistryPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName="G502Lite";
    public static string ExecutablePath=>Path.Combine(AppContext.BaseDirectory,"G502Lite.exe");
    public static string ShortcutPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"G502 Lite.lnk");
    private static object OpenShortcut(out object shell)
    {
        shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")??throw new IOException("Windows Script Host no está disponible."))!;
        try{return ((dynamic)shell).CreateShortcut(ShortcutPath);}
        catch{Marshal.FinalReleaseComObject(shell);throw;}
    }
    public static string BuildCommand(string executable)
    {
        if(string.IsNullOrWhiteSpace(executable)||!Path.IsPathFullyQualified(executable)||!executable.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||executable.IndexOfAny(new[]{'"','\r','\n'})>=0)
            throw new ArgumentException("Se requiere una ruta absoluta a un ejecutable de Windows.");
        return $"\"{executable}\" --tray";
    }
    public static string? ReadCommand()
    {
        if(!File.Exists(ShortcutPath))return null;
        object link=OpenShortcut(out object shell);
        try
        {
            string target=((dynamic)link).TargetPath;
            string arguments=((dynamic)link).Arguments;
            return $"\"{target}\" {arguments}";
        }
        finally{Marshal.FinalReleaseComObject(link);Marshal.FinalReleaseComObject(shell);}
    }
    public static bool IsEnabled()
    {
        using var approval=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder");
        var state=approval?.GetValue(Path.GetFileName(ShortcutPath)) as byte[];
        return !(state is {Length:>0}&&(state[0]==3||state[0]==7))&&string.Equals(ReadCommand(),BuildCommand(ExecutablePath),StringComparison.OrdinalIgnoreCase);
    }
    public static void SetEnabled(bool enabled)
    {
        if(enabled)
        {
            if(!File.Exists(ExecutablePath))throw new IOException("No se encuentra G502Lite.exe junto a los archivos de la aplicación.");
            _=BuildCommand(ExecutablePath);
            Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath)!);
            object link=OpenShortcut(out object shell);
            try
            {
                dynamic shortcut=link;
                shortcut.TargetPath=ExecutablePath;
                shortcut.Arguments="--tray";
                shortcut.WorkingDirectory=AppContext.BaseDirectory;
                shortcut.Description="Iniciar G502 Lite en la bandeja de iconos";
                shortcut.IconLocation=ExecutablePath+",0";
                shortcut.WindowStyle=7;
                shortcut.Save();
            }
            finally{Marshal.FinalReleaseComObject(link);Marshal.FinalReleaseComObject(shell);}
            using var approval=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder",true);
            approval?.DeleteValue(Path.GetFileName(ShortcutPath),false);
        }
        else
        {
            File.Delete(ShortcutPath);
        }
        // Retire the old registration only after the shortcut has been saved successfully.
        using var key=Registry.CurrentUser.OpenSubKey(RegistryPath,true);
        key?.DeleteValue(ValueName,false);
    }
    public static bool IsStartupCommand(string[] args)=>args.Length==2&&args[0] is "--startup-enable" or "--startup-disable" or "--startup-status";
    public static async Task<int> RunCommandAsync(string[] args)
    {
        object result;int code;
        try
        {
            if(args[0]=="--startup-enable")SetEnabled(true);
            if(args[0]=="--startup-disable")SetEnabled(false);
            result=new{ok=true,enabled=IsEnabled(),command=ReadCommand(),shortcut=ShortcutPath};code=0;
        }
        catch(Exception ex){result=new{ok=false,error=ex.Message};code=1;}
        await File.WriteAllTextAsync(Path.GetFullPath(args[1]),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        return code;
    }
}
