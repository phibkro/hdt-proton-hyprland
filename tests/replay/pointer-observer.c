/* Project-owned diagnostic for the private replay display. No input injection. */
#include <X11/Xlib.h>
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>

int main(int argc, char **argv)
{
    if (argc != 2 || getenv("HDT_REPLAY_OBSERVER") == NULL)
        return 64;
    char *end = NULL;
    errno = 0;
    unsigned long window = strtoul(argv[1], &end, 0);
    if (errno != 0 || end == argv[1] || *end != '\0' || window == 0)
        return 64;
    Display *display = XOpenDisplay(NULL);
    if (display == NULL)
        return 1;
    /* Unlike xev's mouse mask, these observations do not select ButtonPress. */
    XSelectInput(display, window,
                 EnterWindowMask | LeaveWindowMask | PointerMotionMask | FocusChangeMask);
    XSync(display, False);
    puts("{\"ready\":true}");
    fflush(stdout);
    for (int count = 0; count < 512; ++count)
    {
        XEvent event;
        XNextEvent(display, &event);
        if (event.type == EnterNotify || event.type == LeaveNotify)
        {
            printf("{\"type\":%d,\"serial\":%lu,\"sent\":%d,\"x\":%d,\"y\":%d,\"mode\":%d,"
                   "\"detail\":%d}\n",
                   event.type, event.xany.serial, event.xany.send_event, event.xcrossing.x_root,
                   event.xcrossing.y_root, event.xcrossing.mode, event.xcrossing.detail);
        }
        else if (event.type == MotionNotify)
        {
            printf("{\"type\":%d,\"serial\":%lu,\"sent\":%d,\"x\":%d,\"y\":%d}\n", event.type,
                   event.xany.serial, event.xany.send_event, event.xmotion.x_root,
                   event.xmotion.y_root);
        }
        else
        {
            printf("{\"type\":%d,\"serial\":%lu,\"sent\":%d}\n", event.type, event.xany.serial,
                   event.xany.send_event);
        }
        fflush(stdout);
    }
    XCloseDisplay(display);
    return 0;
}
