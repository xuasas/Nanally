using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows;

/// <summary>Thread-safe, bounded line buffering; the collection owns dispatcher delivery.</summary>
public sealed class ConsoleRedirectWriter : TextWriter
{
    private readonly ObservableCollection<ActivityLog> logs;
    private readonly StringBuilder line = new();
    private readonly object gate = new();
    public ConsoleRedirectWriter(ObservableCollection<ActivityLog> activityLogs) => logs = activityLogs;
    public override Encoding Encoding => Encoding.UTF8;
    public override void Write(char value)
    {
        lock (gate)
        {
            if (value == '\n') FlushLine();
            else if (value != '\r')
            {
                line.Append(value);
                if (line.Length >= 4096) FlushLine();
            }
        }
    }
    public override void Write(string? value)
    {
        lock (gate) { if (value != null) foreach (char valueChar in value) Write(valueChar); }
    }
    public override void WriteLine(string? value) { lock (gate) { Write(value); FlushLine(); } }
    public override void WriteLine() { lock (gate) FlushLine(); }
    public override void Flush() { lock (gate) FlushLine(); }
    private void FlushLine()
    {
        if (line.Length == 0) return;
        string text = line.ToString(); line.Clear();
        logs.Add(new ActivityLog("console", true, text));
    }
    protected override void Dispose(bool disposing) { if (disposing) Flush(); base.Dispose(disposing); }
}
