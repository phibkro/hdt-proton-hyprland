using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using Microsoft.Diagnostics.Runtime;

static class Program {
    internal enum TargetKind {Replay,Tracker}
    delegate bool DumpCallback(IntPtr parameter,IntPtr input,IntPtr output);
    [StructLayout(LayoutKind.Sequential)] struct CallbackInformation {public DumpCallback Callback;public IntPtr Parameter;}
    [DllImport("dbghelp.dll",SetLastError=true)] static extern bool MiniDumpWriteDump(IntPtr process,uint pid,IntPtr file,uint flags,IntPtr exception,IntPtr user,ref CallbackInformation callback);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll",CharSet=CharSet.Ansi,ExactSpelling=true)] static extern IntPtr GetProcAddress(IntPtr module,string name);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    internal static string Hash(string path) {using(var hash=SHA256.Create()) using(var file=File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(file)).Replace("-","").ToLowerInvariant();}
    internal static void RequireHash(string path,string expected) {
        if(expected.Length!=64 || expected.Any(c=>!Uri.IsHexDigit(c)) || Hash(path)!=expected.ToLowerInvariant()) throw new InvalidDataException("Executable or DAC identity mismatch.");
    }
    static int Main(string[] args) {
        // Every capture has a caller-owned, exclusive result file, even on error.
        bool lease=args.Length==7 && (args[0]=="debug-snapshot" || args[0]=="hdt-debug-snapshot" || args[0]=="debug-timeout" || args[0]=="debug-crash");
        bool reader=args.Length==9 && args[0]=="lease-read";
        if(!((args.Length==6 && (args[0]=="dump" || args[0]=="snapshot")) || (args.Length==5 && args[0]=="read") || (args.Length==2 && args[0]=="mouse-state-probe") || lease || reader)) {Console.Error.WriteLine("Use dump PID EXE_SHA OUTPUT_DMP RESULT_JSON MAX_MS, snapshot PID EXE_SHA DAC DAC_SHA RESULT_JSON, read DUMP DAC DAC_SHA RESULT_JSON, or debug-snapshot PID EXE_SHA DAC DAC_SHA RESULT_JSON MAX_MS.");return 2;}
        string output=args[args.Length-1];
        if(args[0]=="dump") output=args[4];
        if(lease || reader) output=args[5];
        FileStream sink;
        try {sink=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.Read);} catch(Exception error) {Console.Error.WriteLine(error.GetType().FullName);return 2;}
        object result;
        int code=1;
        try {
            switch(args[0]) {
                case "mouse-state-probe":
                    var probeClock=Stopwatch.StartNew();
                    // Read only mouse-button high bits. Never send input or read keys.
                    bool right=(GetAsyncKeyState(2)&0x8000)!=0;
                    bool left=(GetAsyncKeyState(1)&0x8000)!=0;
                    result=new {status="captured",operation=args[0],rightDown=right,leftDown=left,durationMS=probeClock.ElapsedMilliseconds};code=0;break;
                case "hdt-debug-snapshot":case "debug-snapshot":case "debug-timeout":case "debug-crash":
                    result=DebugLease.Capture(args,out code);break;
                case "lease-read":result=DebugLease.Read(args,out code);break;
                case "dump":
                    if(args.Length!=6) throw new ArgumentException("Invalid dump arguments.");
                    using(var target=ValidateTarget(args[1],args[2],TargetKind.Replay)) {
                        int milliseconds=int.Parse(args[5]);if(milliseconds<1 || milliseconds>30000) throw new ArgumentException("Capture budget must be 1..30000 ms.");
                        using(var dump=new FileStream(args[3],FileMode.CreateNew,FileAccess.Write,FileShare.Read)) {
                            var clock=Stopwatch.StartNew();
                            DumpCallback callback=(parameter,input,response)=>{
                                // MINIDUMP_CALLBACK_INPUT x64: CallbackType at16;
                                // CancelCallback=6; output is CheckCancel, Cancel.
                                if(Marshal.ReadInt32(input,16)==6) {Marshal.WriteInt32(response,0,1);Marshal.WriteInt32(response,4,clock.ElapsedMilliseconds>=milliseconds?1:0);}
                                return true;
                            };
                            var callbacks=new CallbackInformation {Callback=callback};
                            bool success=MiniDumpWriteDump(target.Handle,(uint)target.Id,dump.SafeFileHandle.DangerousGetHandle(),0x00001802,IntPtr.Zero,IntPtr.Zero,ref callbacks);
                            GC.KeepAlive(callback);
                            if(!success) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                            dump.Flush(true);
                            result=new {status="captured",consistency="minidump-provider",targetPid=target.Id,targetStartTicks=target.StartTime.ToUniversalTime().Ticks,executableSHA256=args[2],dumpBytes=dump.Length,durationMS=clock.ElapsedMilliseconds};
                            code=0;
                        }
                    }
                    break;
                case "snapshot":
                    if(args.Length!=6) throw new ArgumentException("Invalid snapshot arguments.");
                    RequireHash(args[3],args[4]);
                    var kernel=GetModuleHandle("kernel32.dll");
                    if(new[]{"PssCaptureSnapshot","PssQuerySnapshot","PssFreeSnapshot"}.Any(name=>GetProcAddress(kernel,name)==IntPtr.Zero)) throw new PlatformNotSupportedException("Required PSS snapshot APIs are unavailable.");
                    using(var target=ValidateTarget(args[1],args[2],TargetKind.Replay))
                    using(var data=DataTarget.CreateSnapshotAndAttach(target.Id,Options(args[3],args[4]))) result=Read(data,args[3],args[4],"clrmd-process-snapshot",out code);
                    break;
                case "read":
                    if(args.Length!=5) throw new ArgumentException("Invalid read arguments.");
                    RequireHash(args[2],args[3]);
                    using(var data=DataTarget.LoadDump(args[1],Options(args[2],args[3]))) result=Read(data,args[2],args[3],"immutable-minidump",out code);
                    break;
                default:throw new ArgumentException("Unknown operation.");
            }
        } catch(Exception error) {
            code=1;
            result=new {status="failed",operation=args[0],errorType=error.GetType().FullName,hresult=error.HResult,message=error.Message};
        }
        using(sink) using(var writer=new StreamWriter(sink)) writer.WriteLine(new JavaScriptSerializer {MaxJsonLength=16*1024*1024,RecursionLimit=128}.Serialize(result));
        return code;
    }
    internal static Process ValidateTarget(string rawPid,string expectedHash,TargetKind kind) {
        int pid=int.Parse(rawPid);
        if(pid<=0 || pid==Process.GetCurrentProcess().Id) throw new ArgumentException("Invalid target PID.");
        var target=Process.GetProcessById(pid);
        try {
            // Hold the Windows process handle through capture to retain identity.
            var handle=target.Handle;
            string executable=target.MainModule.FileName;
            string name=Path.GetFileName(executable);
            string required=kind==TargetKind.Tracker?"HearthstoneDeckTracker.exe":"ReplayHost.exe";
            if(name!=required) throw new ArgumentException("This operation requires "+required+". Live HDT dumps and fault probes are disabled.");
            RequireHash(executable,expectedHash);
            return target;
        } catch {target.Dispose();throw;}
    }
    internal static DataTargetOptions Options(string dac,string expectedHash) {
        return new DataTargetOptions {
            FileLocator=new NoFiles(),SymbolPaths=Array.Empty<string>(),
            // Wine's signature-verification path failed for the installed DAC.
            // Grant only the caller's explicit path AND hash; runtime matching
            // remains enabled by CreateRuntime(dac). No alternate DAC is loaded.
            DacSignatureVerificationOverride=path=>{
                if(Path.GetFullPath(path)!=Path.GetFullPath(dac)) throw new InvalidDataException("Unexpected DAC path.");
                RequireHash(path,expectedHash);return false;
            }
        };
    }
    sealed class NoFiles:IFileLocator {
        public string FindPEImage(string name,int timestamp,int size,bool check) {return null;}
        public string FindPEImage(string name,SymbolProperties properties,ImmutableArray<byte> id,OSPlatform platform,bool check) {return null;}
    }
    internal static object Read(DataTarget data,string dac,string dacHash,string consistency,out int code) {
        if(data.ClrVersions.Length!=1) throw new InvalidDataException("Expected exactly one CLR runtime.");
        using(var runtime=data.ClrVersions[0].CreateRuntime(dac)) {
            bool partial=false;
            var threads=new List<object>();
            var owners=new Dictionary<ulong,uint>();
            foreach(var thread in runtime.Threads) {
                if(threads.Count==256) throw new InvalidDataException("Thread limit exceeded.");
                owners[thread.Address]=thread.OSThreadId;
                try {
                    var frames=thread.EnumerateStackTrace(false,256).Select(frame=>new {ip=frame.InstructionPointer.ToString("x"),sp=frame.StackPointer.ToString("x"),method=frame.Method?.Signature,kind=frame.Kind.ToString()}).ToArray();
                    bool truncated=frames.Length==256;
                    bool unreadable=thread.IsAlive && frames.Length==0;
                    // Wine can return a native frame without any recoverable
                    // managed method. That is not evidence of a complete stack.
                    bool unresolved=thread.IsAlive && frames.Length>0 && frames.All(frame=>frame.method==null);
                    partial|=truncated||unreadable||unresolved;
                    threads.Add(new {osThreadId=thread.OSThreadId,managedThreadId=thread.ManagedThreadId,address=thread.Address.ToString("x"),alive=thread.IsAlive,lockCount=thread.LockCount,status=truncated?"truncated":unreadable?"unreadable":unresolved?"unresolved":"captured",frames=frames});
                } catch(Exception error) {partial=true;threads.Add(new {osThreadId=thread.OSThreadId,status="failed",errorType=error.GetType().FullName});}
            }
            var locks=new List<object>();int seen=0;
            foreach(var block in runtime.Heap.EnumerateSyncBlocks()) {
                if(++seen>16384) throw new InvalidDataException("Sync-block limit exceeded.");
                if(!block.IsMonitorHeld) continue;
                uint owner;
                locks.Add(new {objectAddress=block.Object.ToString("x"),ownerAddress=block.HoldingThreadAddress.ToString("x"),ownerOSThreadId=owners.TryGetValue(block.HoldingThreadAddress,out owner)?(uint?)owner:null,recursion=block.RecursionCount,waitingThreads=block.WaitingThreadCount});
            }
            code=partial?3:0;
            return new {status=partial?"partial":"captured",consistency=consistency,clrmdVersion=typeof(DataTarget).Assembly.GetName().Version.ToString(),dacTrust="explicit-path-and-sha256",dacSHA256=dacHash,clrVersion=data.ClrVersions[0].Version.ToString(),syncBlocksExamined=seen,threads=threads,heldMonitors=locks};
        }
    }
}
