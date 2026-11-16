using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Diagnostics.Runtime;

// Native API semantics checked against Wine ntdll/process.c and kernelbase/debug.c.
// NtDebugActiveProcess omits DbgUiIssueRemoteBreakin: no injected break-in thread.
// The debug object is created with flags=0, never DEBUG_KILL_ON_CLOSE.
static class DebugLease {
    [StructLayout(LayoutKind.Sequential)] struct Attributes {
        public int Length;public IntPtr Root,ObjectName;public uint Flags;public IntPtr Security,Quality;
    }
    [DllImport("ntdll.dll")] static extern int NtCreateDebugObject(out IntPtr handle,uint access,ref Attributes attributes,uint flags);
    [DllImport("ntdll.dll")] static extern int NtDebugActiveProcess(IntPtr process,IntPtr debug);
    [DllImport("ntdll.dll")] static extern int NtRemoveProcessDebug(IntPtr process,IntPtr debug);
    [DllImport("ntdll.dll")] static extern void DbgUiSetThreadDebugObject(IntPtr debug);
    [DllImport("ntdll.dll")] static extern IntPtr DbgUiGetThreadDebugObject();
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool WaitForDebugEvent(IntPtr data,uint milliseconds);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool CheckRemoteDebuggerPresent(IntPtr process,out bool present);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    static void Check(int status,string operation) {if(status!=0) throw new InvalidOperationException(operation+" NTSTATUS 0x"+status.ToString("x8"));}
    static string Quote(string value) {
        if(value.Contains("\"")) throw new ArgumentException("Unexpected quote in argument.");
        return "\""+value+"\"";
    }
    public static object Capture(string[] args,out int code) {
        code=1;
        int budget=int.Parse(args[6]);if(budget<1||budget>10000) throw new ArgumentException("Lease budget must be 1..10000 ms.");
        Program.RequireHash(args[3],args[4]);
        bool tracker=args[0]=="hdt-debug-snapshot";
        using(var target=Program.ValidateTarget(args[1],args[2],tracker?Program.TargetKind.Tracker:Program.TargetKind.Replay)) {
            bool present;
            if(!CheckRemoteDebuggerPresent(target.Handle,out present)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if(present || DbgUiGetThreadDebugObject()!=IntPtr.Zero) throw new InvalidOperationException("A debugger already owns this process or thread.");
            string token="Local\\HDTManagedLease-"+Guid.NewGuid().ToString("N");
            string workerOutput=args[5]+".reader.json";
            using(var armed=new EventWaitHandle(false,EventResetMode.ManualReset,token+"-armed"))
            using(var read=new EventWaitHandle(false,EventResetMode.ManualReset,token+"-read")) {
                var start=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location) {
                    UseShellExecute=false,CreateNoWindow=true,
                    Arguments=String.Join(" ",new[]{"lease-read",args[1],args[2],args[3],args[4],workerOutput,token,Process.GetCurrentProcess().Id.ToString(),tracker?"hdt-read":args[0]=="debug-timeout"?"stall":"read"}.MapQuote())
                };
                using(var worker=Process.Start(start)) {
                    IntPtr debug=IntPtr.Zero,data=IntPtr.Zero;bool attached=false,eventReceived=false;int detached=0;object result=null;
                    var clock=new Stopwatch();
                    try {
                        if(!armed.WaitOne(10000)) throw new TimeoutException("Reader did not arm before attachment.");
                        var attributes=new Attributes {Length=Marshal.SizeOf(typeof(Attributes))};
                        Check(NtCreateDebugObject(out debug,0x1f000f,ref attributes,0),"Create non-killing debug object");
                        DbgUiSetThreadDebugObject(debug);
                        clock.Start();
                        Check(NtDebugActiveProcess(target.Handle,debug),"Attach debug lease");attached=true;
                        data=Marshal.AllocHGlobal(256);
                        if(!WaitForDebugEvent(data,(uint)Math.Min(budget,2000))) throw new Win32Exception(Marshal.GetLastWin32Error());
                        eventReceived=true;
                        if(Marshal.ReadInt32(data,4)!=target.Id) throw new InvalidDataException("Debug event target mismatch.");
                        using(var marker=new StreamWriter(new FileStream(args[5]+".lease-held.json",FileMode.CreateNew,FileAccess.Write,FileShare.Read)))
                            marker.WriteLine(new JavaScriptSerializer().Serialize(new {status="held",targetPid=target.Id,killOnClose=false,utc=DateTime.UtcNow.ToString("o")}));
                        if(args[0]=="debug-crash") Process.GetCurrentProcess().Kill();
                        read.Set();
                        int remaining=Math.Max(0,budget-(int)clock.ElapsedMilliseconds);
                        if(!worker.WaitForExit(remaining)) throw new TimeoutException("Reader exceeded the debug lease budget.");
                        if(worker.ExitCode!=0 && worker.ExitCode!=3) throw new InvalidDataException("Managed reader failed: "+File.ReadAllText(workerOutput));
                        result=new JavaScriptSerializer {MaxJsonLength=16*1024*1024}.DeserializeObject(File.ReadAllText(workerOutput));
                        code=worker.ExitCode;
                    } finally {
                        // Child execution is ours. The tracker is never terminated.
                        // A reader cleanup failure must not skip target release.
                        try {
                            if(!worker.HasExited) {worker.Kill();worker.WaitForExit(2000);}
                        } finally {
                            try {
                                if(attached) detached=NtRemoveProcessDebug(target.Handle,debug);
                            } finally {
                                DbgUiSetThreadDebugObject(IntPtr.Zero);
                                try {
                                    if(data!=IntPtr.Zero) {
                                        int kind=eventReceived?Marshal.ReadInt32(data):0;
                                        if(kind==3 || kind==6) {IntPtr file=Marshal.ReadIntPtr(data,16);if(file!=IntPtr.Zero) CloseHandle(file);}
                                        Marshal.FreeHGlobal(data);
                                    }
                                } finally {
                                    if(debug!=IntPtr.Zero) CloseHandle(debug);
                                    clock.Stop();
                                }
                            }
                        }
                        Check(detached,"Detach debug lease");
                    }
                    return new {status=code==0?"captured":"partial",consistency="native-debug-event-held",killOnClose=false,
                        targetPid=target.Id,targetStartTicks=target.StartTime.ToUniversalTime().Ticks,executableSHA256=args[2],pauseMS=clock.ElapsedMilliseconds,reader=result};
                }
            }
        }
    }
    public static object Read(string[] args,out int code) {
        using(var target=Program.ValidateTarget(args[1],args[2],args[8]=="hdt-read"?Program.TargetKind.Tracker:Program.TargetKind.Replay))
        using(var guard=Process.GetProcessById(int.Parse(args[7]))) {
            Program.RequireHash(guard.MainModule.FileName,Program.Hash(Assembly.GetExecutingAssembly().Location));
            Program.RequireHash(args[3],args[4]);
            using(var armed=EventWaitHandle.OpenExisting(args[6]+"-armed"))
            using(var read=EventWaitHandle.OpenExisting(args[6]+"-read")) {
                armed.Set();
                if(!read.WaitOne(10000) || guard.HasExited) throw new InvalidOperationException("No live debug lease controller.");
                bool debugged;
                if(!CheckRemoteDebuggerPresent(target.Handle,out debugged) || !debugged) throw new InvalidOperationException("Target is not debugger-held.");
                if(args[8]=="stall") Thread.Sleep(Timeout.Infinite);
                // Passive reads are valid here only under the controller's held
                // debug event. The controller validates completion before detach.
                using(var data=DataTarget.AttachToProcess(target.Id,false,Program.Options(args[3],args[4])))
                    return Program.Read(data,args[3],args[4],"native-debug-event-held",out code);
            }
        }
    }
    static string[] MapQuote(this string[] values) {
        var result=new string[values.Length];for(int i=0;i<values.Length;i++) result[i]=Quote(values[i]);return result;
    }
}
