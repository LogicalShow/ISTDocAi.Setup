using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Microsoft.Data.SqlClient;
using ISTDocAi.Provisioning;

namespace ISTDocAi.DevSetup;

public record ProcessResult(int Code, byte[] Output);

// All machine operations have a replaceable boundary; tests never import/trust certificates.
public class SetupEnvironment
{
    public virtual string ExecutableFolder => AppContext.BaseDirectory;
    public virtual string Account => WindowsIdentity.GetCurrent().Name;
    public virtual bool Elevated => OperatingSystem.IsWindows() && new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public virtual bool PauseAtExit => OperatingSystem.IsWindows() && NativeConsole.Alone();
    public virtual SqlConnection Connection(string connection) => new(CompanyIdentityCheck.AdjustConnectionString(connection));
    public virtual bool CertificateReady(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
        try { return matches.Any(c => c.HasPrivateKey && DateTime.Now >= c.NotBefore && DateTime.Now < c.NotAfter); }
        finally { foreach (var cert in matches) cert.Dispose(); }
    }
    public virtual string ImportCertificate(string path, string password)
    {
        using var cert = LoadPfx(path, password);
        if (!cert.HasPrivateKey || DateTime.Now < cert.NotBefore || DateTime.Now >= cert.NotAfter) throw new CliRefusal("PFX password is wrong or the file is not a valid PFX.");
        using var rsa = cert.GetRSAPrivateKey();
        if (rsa is null) throw new CliRefusal("PFX password is wrong or the file is not a valid PFX.");
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite); store.Add(cert);
        return cert.Thumbprint;
    }
    private static X509Certificate2 LoadPfx(string path, string password)
    {
        try { return X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet); }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or IOException or ArgumentException)
        { throw new CliRefusal("PFX password is wrong or the file is not a valid PFX."); }
    }
    public virtual ProcessResult Process(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new CliEnvironment("Could not start required tool.");
        using var bytes = new MemoryStream();
        var output = process.StandardOutput.BaseStream.CopyToAsync(bytes);
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60000)) { process.Kill(true); throw new CliEnvironment("Required tool timed out."); }
        Task.WhenAll(output, error).GetAwaiter().GetResult();
        return new(process.ExitCode, bytes.ToArray());
    }
}
internal static class NativeConsole
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList([System.Runtime.InteropServices.Out] uint[] list, uint count);
    internal static bool Alone() => GetConsoleProcessList(new uint[2], 2)==1;
}
