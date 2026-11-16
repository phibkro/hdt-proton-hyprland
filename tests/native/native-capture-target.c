/* Project-owned debugger lifetime fixture; no Wine or X11 implementation. */
#include <signal.h>
#include <stdio.h>
#include <sys/prctl.h>
#include <unistd.h>

// The debugger contract requires the actual Xlib symbol spelling.
// NOLINTNEXTLINE(bugprone-reserved-identifier)
__attribute__((noinline)) void _XDefaultError(void *display, unsigned char *event)
{
    (void)display;
    (void)event;
    __asm__ volatile("" ::: "memory");
}
__attribute__((noinline)) void XGetDeviceButtonMapping(void) { __asm__ volatile("" ::: "memory"); }
__attribute__((noinline)) void XOpenDevice(void) { __asm__ volatile("" ::: "memory"); }
int main(int argc, char **argv)
{
    unsigned char event[64] = {0};
    (void)argv;
    if (prctl(PR_SET_NAME, "HearthstoneDeck") != 0 ||
        prctl(PR_SET_PTRACER, PR_SET_PTRACER_ANY) != 0)
        return 2;
    puts("ready");
    fflush(stdout);
    for (int i = 0;; ++i)
    {
        sleep(1);
        if (argc > 1 && i >= 3)
            _XDefaultError(NULL, event);
    }
}
