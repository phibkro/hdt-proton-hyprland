/* SPDX-License-Identifier: MIT
 * Project-owned fixture for the XOpenDevice / mapping race in Valve Wine's
 * dlls/winex11.drv/mouse.c. Uses public libXi APIs; no upstream code copied.
 * Run only on the disposable server created by xinput-race.ts.
 */
#include <X11/Xlib.h>
#include <X11/extensions/XInput.h>
#include <X11/extensions/XInput2.h>
#include <X11/extensions/XIproto.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static Display *expected_display;
static unsigned long expected_serial;
static int opcode, error_base, caught;
static XErrorHandler previous;

static int scoped_error(Display *display, XErrorEvent *event)
{
    if(display == expected_display && event->serial == expected_serial &&
       event->request_code == opcode && event->minor_code == X_GetDeviceButtonMapping &&
       event->error_code == error_base + XI_BadDevice) {
        caught++;
        fprintf(stdout,"caught mapping BadDevice serial=%lu resource=%lu\n",event->serial,event->resourceid);
        return 0;
    }
    return previous(display,event);
}

int main(int argc, char **argv)
{
    if(argc != 3 || !getenv("HDT_XINPUT_REPRO")) return 64;
    if(strcmp(argv[2],"fatal") && strcmp(argv[2],"guarded") && strcmp(argv[2],"valid") &&
       strcmp(argv[2],"unrelated") && strcmp(argv[2],"wrong-serial") && strcmp(argv[2],"bad-match")) return 64;
    setvbuf(stdout,NULL,_IONBF,0);
    Display *display = XOpenDisplay(argv[1]);
    if(!display) return 65;
    int event_base,major=2,minor=0;
    if(!XQueryExtension(display,"XInputExtension",&opcode,&event_base,&error_base) ||
       XIQueryVersion(display,&major,&minor) != Success) return 66;
    XIAddMasterInfo add = {.type=XIAddMaster,.name="HDT fixture",.send_core=True,.enable=True};
    if(XIChangeHierarchy(display,(XIAnyHierarchyChangeInfo *)&add,1) != Success) return 67;
    XSync(display,False);
    int count,master=0,slave=0,keyboard=0;
    XIDeviceInfo *devices=XIQueryDevice(display,XIAllDevices,&count);
    for(int i=0;i<count;i++) {
        if(!strcmp(devices[i].name,"HDT fixture pointer")) master=devices[i].deviceid;
        if(!strcmp(devices[i].name,"HDT fixture XTEST pointer")) slave=devices[i].deviceid;
        if(!strcmp(devices[i].name,"HDT fixture XTEST keyboard")) keyboard=devices[i].deviceid;
    }
    XIFreeDeviceInfo(devices);
    if(!master || !slave || !keyboard) return 68;
    XDevice *device=XOpenDevice(display,slave);
    if(!device) return 69;
    unsigned char map[256];
    int before=XGetDeviceButtonMapping(display,device,map,sizeof(map));
    printf("opened device=%d initial_buttons=%d\n",slave,before);
    if(before <= 0) return 70;
    int valid=!strcmp(argv[2],"valid") || !strcmp(argv[2],"bad-match");
    if(!strcmp(argv[2],"bad-match")) {
        XCloseDevice(display,device);
        device=XOpenDevice(display,keyboard);
        if(!device) return 69;
    }
    if(!valid) {
        XIRemoveMasterInfo remove={.type=XIRemoveMaster,.deviceid=master,.return_mode=XIAttachToMaster,.return_pointer=2,.return_keyboard=3};
        if(XIChangeHierarchy(display,(XIAnyHierarchyChangeInfo *)&remove,1) != Success) return 71;
        XSync(display,False);
        devices=XIQueryDevice(display,XIAllDevices,&count);
        int found=0;
        for(int i=0;i<count;i++) if(devices[i].deviceid==slave) found=1;
        XIFreeDeviceInfo(devices);
        if(found) return 72;
        printf("device removed after successful open\n");
    }
    int guarded=strcmp(argv[2],"fatal")!=0;
    if(guarded) {
        XSync(display,False);
        expected_display=display;
        expected_serial=NextRequest(display);
        if(!strcmp(argv[2],"wrong-serial")) expected_serial++;
        previous=XSetErrorHandler(scoped_error);
    }
    if(!strcmp(argv[2],"unrelated")) XDestroyWindow(display,0x12345678);
    int after=XGetDeviceButtonMapping(display,device,map,sizeof(map));
    XSync(display,False);
    if(guarded) XSetErrorHandler(previous);
    printf("mapping returned=%d caught=%d\n",after,caught);
    if(valid) XCloseDevice(display,device);
    /* The removed device has no server resource left. Closing the display and
     * exiting the fixture releases client storage without another stale request. */
    XCloseDisplay(display);
    return valid ? (after>0 && !caught ? 0:73) : (after==0 && caught==1 ? 0:74);
}
