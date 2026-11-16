using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

// Account-free replay fixture only. Never compiled into the compatibility plugin.
static class ManagedSnapshotFixture {
    static readonly object gate=new object();
    static readonly ManualResetEvent ready=new ManualResetEvent(false);
    static string release;
    public static void Start(string directory) {
        release=Path.Combine(directory,"release-managed-lock");
        if(File.Exists(release)) throw new InvalidOperationException("Release file already exists");
        new Thread(HoldMonitorUntilRelease) {IsBackground=true,Name="replay-monitor-owner"}.Start();
        if(!ready.WaitOne(2000)) throw new InvalidOperationException("Monitor owner did not start");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void HoldMonitorUntilRelease() {
        lock(gate) {
            ready.Set();
            int tick=0;
            while(!File.Exists(release)) {
                if(++tick%5==0) File.WriteAllText(Path.Combine(Path.GetDirectoryName(release),"managed-owner-heartbeat.txt"),tick.ToString());
                Thread.Sleep(20);
            }
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void EnterContendedMonitor() {lock(gate) {GC.KeepAlive(gate);}}
}
