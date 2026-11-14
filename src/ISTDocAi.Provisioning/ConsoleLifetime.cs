using System.Runtime.InteropServices;

namespace ISTDocAi.Provisioning;

public static class ConsoleLifetime
{
    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList([Out] uint[] processList, uint processCount);

    public static async Task<int> Run(string[] args, bool kit, IProvisionConsole console)
    {
        var alone = OperatingSystem.IsWindows() && GetConsoleProcessList(new uint[2], 2) == 1;
        try { return kit ? await ProvisionRunner.Run(args, true, console) : await ServerSetupRunner.Execute(args, new ServerEnvironment(), console); }
        finally { Pause(alone, console); }
    }

    public static void Pause(bool alone, IProvisionConsole console)
    {
        if (!alone) return;
        console.WriteLine("Press Enter to close");
        console.ReadLine();
    }
}
