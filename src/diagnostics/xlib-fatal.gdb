set pagination off
set confirm off
set auto-load off
set debuginfod enabled off
set print frame-arguments none
set print entry-values no
set print elements 0
set breakpoint pending on
set follow-fork-mode parent
set detach-on-fork on
set print thread-events off
handle SIGUSR1 SIGUSR2 SIGPIPE SIGALRM SIGSEGV SIGILL SIGFPE nostop noprint pass
set architecture i386:x86-64
hbreak *_XDefaultError
commands
silent
echo HDT_NATIVE_XLIB_FATAL_BEGIN\n
info registers rip rsp rbp rdi rsi
x/3ub $rsi+32
bt 24
echo HDT_NATIVE_XLIB_FATAL_END\n
detach
quit
end
set $hdt_mapping_queries = 0
hbreak *XGetDeviceButtonMapping
set $hdt_query_breakpoint = $bpnum
commands
silent
set $hdt_mapping_queries = $hdt_mapping_queries + 1
printf "HDT_MAPPING_QUERY count=%u thread=%u display=0x%llx device=%llu\n", $hdt_mapping_queries, $_thread, (unsigned long long)$rdi, *(unsigned long long*)$rsi
if $hdt_mapping_queries >= 1000
disable $hdt_query_breakpoint
end
continue
end
set $hdt_open_queries = 0
hbreak *XOpenDevice
set $hdt_open_breakpoint = $bpnum
commands
silent
set $hdt_open_queries = $hdt_open_queries + 1
printf "HDT_DEVICE_OPEN count=%u thread=%u display=0x%llx requested=%llu\n", $hdt_open_queries, $_thread, (unsigned long long)$rdi, (unsigned long long)$rsi
if $hdt_open_queries >= 1000
disable $hdt_open_breakpoint
end
continue
end
