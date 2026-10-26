using System;
using System.IO;
using System.Linq;
using System.Text;

namespace VPet_Simulator.Windows;

internal sealed record SaveSnapshot(long Sequence, string Root, string Prefix, int Number, int Retained,
    string Settings, string? Game, int BackupBucket);

/// <summary>Immutable UI snapshot, serialized disk commits, same-directory atomic replacement.</summary>
internal sealed class NanallyPersistence
{
    private readonly object gate = new();
    private long committed;
    public void Commit(SaveSnapshot snapshot)
    {
        lock (gate)
        {
            if (snapshot.Sequence <= committed) return;
            string settings = Path.Combine(snapshot.Root, $"Setting{snapshot.Prefix}.lps");
            if (snapshot.Game != null)
            {
                string saves = Path.Combine(snapshot.Root, "Saves");
                string backups = Path.Combine(snapshot.Root, "Saves_BKP");
                Directory.CreateDirectory(saves); Directory.CreateDirectory(backups);
                // Commit the game before advancing settings. Never remove old saves before a successful write.
                AtomicWrite(Path.Combine(saves, $"Save{snapshot.Prefix}_{snapshot.Number}.lps"), snapshot.Game);
                AtomicWrite(Path.Combine(backups, $"Save{snapshot.Prefix}_{snapshot.BackupBucket:X}.lps"), snapshot.Game);
                AtomicWrite(settings, snapshot.Settings, settings[..^4] + ".bkp");
                var old = Directory.GetFiles(saves, $"Save{snapshot.Prefix}_*.lps")
                    .OrderByDescending(file => int.TryParse(Path.GetFileNameWithoutExtension(file).Split('_').Last(), out int n) ? n : -1)
                    .Skip(Math.Max(1, snapshot.Retained)).ToArray();
                foreach (string file in old) File.Delete(file);
            }
            else AtomicWrite(settings, snapshot.Settings, settings[..^4] + ".bkp");
            committed = snapshot.Sequence;
        }
    }

    internal static void AtomicWrite(string path, string text, string? backup = null)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                stream.Write(bytes); stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, backup);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
