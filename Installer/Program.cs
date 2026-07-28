namespace CNJ.TowerDebt.Setup;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm(InstallerStrings.DetectInitialLanguage()));
    }
}
