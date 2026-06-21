namespace Rxdk.XbWatson;

internal static class WatsonBeep
{
    public static void Exclamation()
    {
        if (OperatingSystem.IsWindows())
            System.Media.SystemSounds.Exclamation.Play();
        else
            Console.Write('\a');
    }

    public static void Beep()
    {
        if (OperatingSystem.IsWindows())
            System.Media.SystemSounds.Beep.Play();
        else
            Console.Write('\a');
    }
}
