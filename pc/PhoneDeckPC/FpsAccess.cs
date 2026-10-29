using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace PhoneDeckPC;

/// <summary>
/// « Activer les FPS » : Windows ne donne les traces des images qu'aux administrateurs et au groupe
/// « Utilisateurs du journal de performances ». On demande l'autorisation une fois (fenêtre de Windows « Oui / Non ») :
/// KeyraDeck ajoute l'utilisateur à ce groupe (les FPS marcheront sans rien faire après la prochaine ouverture de session)
/// et se relance tout de suite en administrateur pour que les FPS marchent dès maintenant.
/// </summary>
public static class FpsAccess
{
    private const string TakeoverEvent = @"Local\KeyraDeck-FpsTakeover";
    private const string PerfLogUsersSid = "S-1-5-32-559";
    private const int ExitGrantedOtherUser = 3, ExitFailed = 1;
    private static int _busy;

    public static bool IsAdmin
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }
    }

    /// <summary>
    /// Demande l'autorisation sur le PC. null : accordée, KeyraDeck se relance (<paramref name="exit"/> ferme celui-ci) ;
    /// sinon un message pour l'utilisateur.
    /// </summary>
    public static async Task<string?> RequestAsync(bool minimized, Action exit)
    {
        if (!OperatingSystem.IsWindows()) return "Les FPS ne se mesurent que sous Windows.";
        if (IsAdmin) return "KeyraDeck est déjà lancé en administrateur : lance ton jeu, les FPS s'affichent.";
        if (Interlocked.Exchange(ref _busy, 1) == 1) return "Déjà demandé : réponds « Oui » sur l'écran du PC.";
        try
        {
            var exe = Environment.ProcessPath;
            var sid = WindowsIdentity.GetCurrent().User?.Value;
            if (exe == null || sid == null) return "Impossible de relancer KeyraDeck.";
            using var ev = new EventWaitHandle(false, EventResetMode.ManualReset, TakeoverEvent);
            ev.Reset();
            var args = $"--fps-grant {sid} --takeover {Environment.ProcessId}" + (minimized ? " --minimized" : "");
            Process? child;
            try { child = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, Verb = "runas" }); }
            catch (Win32Exception) { return "Autorisation refusée sur le PC (« Non » dans la fenêtre de Windows)."; }
            if (child == null) return "Impossible de relancer KeyraDeck.";

            // Le nouveau KeyraDeck (administrateur) prend la relève, ou s'arrête s'il tourne sous un autre compte Windows.
            for (var i = 0; i < 600; i++)
            {
                if (ev.WaitOne(0))
                {
                    _ = Task.Delay(300).ContinueWith(_ => exit());
                    _ = Task.Delay(5000).ContinueWith(_ => Environment.Exit(0));
                    return null;
                }
                if (child.HasExited)
                    return child.ExitCode == ExitGrantedOtherUser
                        ? "C'est fait. Ferme ta session Windows puis rouvre-la (ou redémarre le PC) : les FPS s'afficheront."
                        : "Windows n'a pas permis d'activer les FPS. Essaie de lancer KeyraDeck avec « Exécuter en tant qu'administrateur ».";
                await Task.Delay(100);
            }
            return "Pas de réponse : la fenêtre de Windows attend peut-être sur le PC.";
        }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    /// <summary>
    /// Au démarrage : si on a été relancé par <see cref="RequestAsync"/>, on ajoute l'utilisateur au groupe puis on prend la relève
    /// de l'ancien KeyraDeck. true : ce programme doit s'arrêter tout de suite.
    /// </summary>
    public static bool HandleElevatedStart(string[] args)
    {
        var at = Array.IndexOf(args, "--fps-grant");
        if (at < 0 || at + 1 >= args.Length || !OperatingSystem.IsWindows()) return false;
        var sid = args[at + 1];
        var granted = AddToPerfLogUsers(sid);

        // Autre compte (mot de passe d'un administrateur tapé par un utilisateur standard) : ses réglages ne sont pas les mêmes,
        // on ne prend pas la relève. Le groupe suffira après la prochaine ouverture de session.
        if (WindowsIdentity.GetCurrent().User?.Value != sid)
        {
            Environment.ExitCode = granted ? ExitGrantedOtherUser : ExitFailed;
            return true;
        }

        try { using var ev = EventWaitHandle.OpenExisting(TakeoverEvent); ev.Set(); } catch { }
        var old = Array.IndexOf(args, "--takeover");
        if (old >= 0 && old + 1 < args.Length && int.TryParse(args[old + 1], out var pid))
        {
            try { using var p = Process.GetProcessById(pid); p.WaitForExit(15000); } catch { }
        }
        return false;
    }

    private static bool AddToPerfLogUsers(string sid)
    {
        try
        {
            var group = new SecurityIdentifier(PerfLogUsersSid).Translate(typeof(NTAccount)).Value;
            group = group[(group.LastIndexOf('\\') + 1)..]; // « BUILTIN\Utilisateurs du journal de performances »
            if (!ConvertStringSidToSidW(sid, out var psid)) return false;
            try
            {
                var member = psid;
                var rc = NetLocalGroupAddMembers(null, group, 0, ref member, 1);
                return rc == 0 || rc == ERROR_MEMBER_IN_ALIAS;
            }
            finally { LocalFree(psid); }
        }
        catch { return false; }
    }

    private const uint ERROR_MEMBER_IN_ALIAS = 1378;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ConvertStringSidToSidW(string sid, out IntPtr psid);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);
    // LOCALGROUP_MEMBERS_INFO_0 = un seul pointeur vers le SID.
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint NetLocalGroupAddMembers(string? server, string group, uint level, ref IntPtr buf, uint count);
}
