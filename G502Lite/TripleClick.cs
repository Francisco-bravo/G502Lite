using System.Runtime.InteropServices;
using G502Lite.Device;

namespace G502Lite;

internal static class TripleClick
{
    // F24 is reserved as the mouse's host-side macro trigger, never a global hotkey.
    public const string Binding="80020073";
    public const string Name="Triple clic izquierdo";
    public static bool ShouldRun(ButtonActivity value,int[] slots)=>
        value.Binding==Binding&&value.Pressed==true&&slots.Any(x=>x is >=2 and <=11);

    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput
    {
        public int X,Y;
        public uint Data,Flags,Time;
        public UIntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Input
    {
        public uint Type;
        public MouseInput Mouse;
    }
    [DllImport("user32.dll",SetLastError=true)] private static extern uint SendInput(uint count,Input[] inputs,int size);
    internal static Input[] Sequence()=>Enumerable.Range(0,6).Select(i=>new Input{Mouse=new MouseInput{Flags=i%2==0?2u:4u}}).ToArray();
    internal static void Execute(Func<Input[],uint>? send=null)
        =>Send(Sequence(),send);
    internal static void ExecuteSingle(Func<Input[],uint>? send=null)
        =>Send(Sequence().Take(2).ToArray(),send);
    private static void Send(Input[] sequence,Func<Input[],uint>? send)
    {
        send??=inputs=>SendInput((uint)inputs.Length,inputs,Marshal.SizeOf<Input>());
        var sent=send(sequence);
        if(sent==sequence.Length)return;
        // A partial insertion must not leave the primary button held down.
        if(sent%2==1)send(new[]{new Input{Mouse=new MouseInput{Flags=4}}});
        throw new IOException("Windows no aceptó la secuencia de clics completa. Comprueba si la aplicación de destino se ejecuta como administrador.");
    }
}
