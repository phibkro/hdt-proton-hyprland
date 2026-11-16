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
info sharedlibrary
echo HDT_NATIVE_XLIB_FATAL_END\n
detach
quit
end
