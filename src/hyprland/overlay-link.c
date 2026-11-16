// HDT-only X11 transient relationship. No game writes, focus, geometry or input events.
#include "../desktop/x11-common.h"
int main(int argc,char **argv) {
    if(argc!=5 && argc!=7) fail("expected apply PID GAME_PID CLASS, or restore PID CLASS XID GAME_XID ORIGINAL");
    display=XOpenDisplay(NULL); if(!display) fail("cannot open X display");
    XSetErrorHandler(on_error);
    unsigned long pid=number(argv[2]);
    if(!pid) fail("missing overlay PID");
    if(!strcmp(argv[1],"apply") && argc==5) {
        Window overlay=find(pid,"HearthstoneOverlay",argv[4]);
        Window game=find(number(argv[3]),"Hearthstone",argv[4]);
        Window original=0;
        XGetTransientForHint(display,overlay,&original);
        // Only the overlay's property is changed; the game is a read-only reference.
        XSetTransientForHint(display,overlay,game);
        XSync(display,False);
        Window actual=0;
        if(xerror || !XGetTransientForHint(display,overlay,&actual) || actual!=game) fail("transient relationship readback failed");
        printf("{\"overlay\":%lu,\"game\":%lu,\"original\":%lu}\n",overlay,game,original);
    } else if(!strcmp(argv[1],"restore") && argc==7) {
        Window overlay=number(argv[4]),game=number(argv[5]),original=number(argv[6]),actual=0;
        if(!identity(overlay,pid,"HearthstoneOverlay",argv[3])) { XCloseDisplay(display); return 0; }
        if(!XGetTransientForHint(display,overlay,&actual) || actual!=game) fail("overlay relationship changed; refusing to overwrite");
        if(original) XSetTransientForHint(display,overlay,original);
        else XDeleteProperty(display,overlay,XInternAtom(display,"WM_TRANSIENT_FOR",False));
        XSync(display,False);
        if(xerror) fail("restore failed");
    } else fail("invalid operation");
    XCloseDisplay(display);
    return 0;
}
