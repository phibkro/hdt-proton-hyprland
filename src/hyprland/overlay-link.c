// HDT-only X11 transient relationship. No game writes, focus, geometry or input events.
#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <X11/Xatom.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <errno.h>
#include <stdint.h>
static Display *display;
static int xerror;
static int on_error(Display *d, XErrorEvent *e)
{
    (void)d;
    xerror = e->error_code;
    return 0;
}
static void fail(const char *s)
{
    fprintf(stderr, "HDT overlay link: %s\n", s);
    exit(1);
}
static unsigned long number(const char *s)
{
    char *end;
    errno = 0;
    unsigned long n = strtoul(s, &end, 0);
    if (errno || !*s || *end || *s == '-' || n > UINT32_MAX)
        fail("invalid XID/PID");
    return n;
}
// Window is an Xlib typedef of unsigned long; retain the Xlib API's identifier types.
// NOLINTNEXTLINE(bugprone-easily-swappable-parameters)
static int identity(Window w, unsigned long pid, const char *title, const char *class)
{
    char *name = NULL;
    XClassHint hint = {0};
    unsigned char *value = NULL;
    Atom type;
    int format;
    unsigned long count, left;
    int ok = 0;
    if (!XFetchName(display, w, &name) || !name || strcmp(name, title) != 0)
        goto end;
    if (!XGetClassHint(display, w, &hint) || !hint.res_class || strcmp(hint.res_class, class) != 0)
        goto end;
    if (XGetWindowProperty(display, w, XInternAtom(display, "_NET_WM_PID", False), 0, 1, False,
                           XA_CARDINAL, &type, &format, &count, &left, &value) != Success ||
        type != XA_CARDINAL || format != 32 || count != 1 || !value)
        goto end;
    ok = *(unsigned long *)value == pid;
end:
    if (name)
        XFree(name);
    if (hint.res_name)
        XFree(hint.res_name);
    if (hint.res_class)
        XFree(hint.res_class);
    if (value)
        XFree(value);
    return ok;
}
static Window find(unsigned long pid, const char *title, const char *class)
{
    Window root, parent, *children = NULL, result = 0;
    unsigned int count = 0;
    if (!XQueryTree(display, DefaultRootWindow(display), &root, &parent, &children, &count))
        fail("cannot enumerate windows");
    for (unsigned int i = 0; i < count; i++)
        if (identity(children[i], pid, title, class))
        {
            if (result)
                fail("ambiguous window identity");
            result = children[i];
        }
    if (children)
        XFree(children);
    if (!result)
        fail("expected window not found");
    return result;
}
int main(int argc, char **argv)
{
    if (argc != 5 && argc != 7)
        fail("expected apply PID GAME_PID CLASS, or restore PID CLASS XID GAME_XID ORIGINAL");
    display = XOpenDisplay(NULL);
    if (!display)
        fail("cannot open X display");
    XSetErrorHandler(on_error);
    unsigned long pid = number(argv[2]);
    if (!pid)
        fail("missing overlay PID");
    if (strcmp(argv[1], "apply") == 0 && argc == 5)
    {
        Window overlay = find(pid, "HearthstoneOverlay", argv[4]);
        Window game = find(number(argv[3]), "Hearthstone", argv[4]);
        Window original = 0;
        XGetTransientForHint(display, overlay, &original);
        // Only the overlay's property is changed; the game is a read-only reference.
        XSetTransientForHint(display, overlay, game);
        XSync(display, False);
        Window actual = 0;
        if (xerror || !XGetTransientForHint(display, overlay, &actual) || actual != game)
            fail("transient relationship readback failed");
        printf("{\"overlay\":%lu,\"game\":%lu,\"original\":%lu}\n", overlay, game, original);
    }
    else if (strcmp(argv[1], "restore") == 0 && argc == 7)
    {
        Window overlay = number(argv[4]), game = number(argv[5]), original = number(argv[6]),
               actual = 0;
        if (!identity(overlay, pid, "HearthstoneOverlay", argv[3]))
        {
            XCloseDisplay(display);
            return 0;
        }
        if (!XGetTransientForHint(display, overlay, &actual) || actual != game)
            fail("overlay relationship changed; refusing to overwrite");
        if (original)
            XSetTransientForHint(display, overlay, original);
        else
            XDeleteProperty(display, overlay, XInternAtom(display, "WM_TRANSIENT_FOR", False));
        XSync(display, False);
        if (xerror)
            fail("restore failed");
    }
    else
        fail("invalid operation");
    XCloseDisplay(display);
    return 0;
}
