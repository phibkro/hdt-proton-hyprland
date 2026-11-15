// Read only an identified HDT overlay's compositor backing pixmap, never the root/game.
#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <X11/Xatom.h>
#include <X11/extensions/Xcomposite.h>
#include <X11/extensions/Xrender.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static void fail(const char *message) { fprintf(stderr,"HDT surface probe: %s\n",message); exit(1); }
static long number(const char *s,int base) {
    char *end; long n=strtol(s,&end,base); if(*end) fail("invalid argument"); return n;
}
int main(int argc,char **argv) {
    if(argc!=7) fail("expected XID, PID, screen x, y, width, height");
    Window window=number(argv[1],0); unsigned long pid=number(argv[2],10);
    int x=number(argv[3],10),y=number(argv[4],10),width=number(argv[5],10),height=number(argv[6],10);
    if(!window || !pid || width<1 || height<1 || width>8192 || height>8192) fail("invalid target or rectangle");
    Display *display=XOpenDisplay(NULL); if(!display) fail("cannot open display");
    char *title=NULL;
    if(!XFetchName(display,window,&title) || !title || strcmp(title,"HearthstoneOverlay")) fail("target is not HDT overlay");
    XFree(title);
    Atom type; int format; unsigned long items,remaining; unsigned char *owner=NULL;
    Atom key=XInternAtom(display,"_NET_WM_PID",True);
    if(!key || XGetWindowProperty(display,window,key,0,1,False,XA_CARDINAL,&type,&format,&items,&remaining,&owner)!=Success || format!=32 || items!=1 || !owner || *(unsigned long*)owner!=pid) fail("overlay owner mismatch");
    XFree(owner);
    XWindowAttributes attrs; if(!XGetWindowAttributes(display,window,&attrs) || attrs.map_state!=IsViewable) fail("overlay not viewable");
    XRenderPictFormat *picture=XRenderFindVisualFormat(display,attrs.visual);
    if(!picture || picture->type!=PictTypeDirect || !picture->direct.alphaMask) fail("overlay has no direct alpha channel");
    int rootx,rooty; Window child;
    if(!XTranslateCoordinates(display,window,attrs.root,0,0,&rootx,&rooty,&child)) fail("cannot translate bounds");
    x-=rootx; y-=rooty;
    if(x<0) {width+=x;x=0;} if(y<0) {height+=y;y=0;}
    if(x+width>attrs.width) width=attrs.width-x;
    if(y+height>attrs.height) height=attrs.height-y;
    if(width<1 || height<1) fail("preview does not intersect overlay");
    int major=0,minor=2;
    if(!XCompositeQueryVersion(display,&major,&minor) || (major==0 && minor<2)) fail("Composite unavailable");
    // Does not redirect, clear, redraw, focus, resize, or change any window.
    Pixmap pixmap=XCompositeNameWindowPixmap(display,window);
    XImage *image=XGetImage(display,pixmap,x,y,width,height,AllPlanes,ZPixmap);
    if(!image) fail("cannot read overlay backing pixmap");
    unsigned long clear=0,opaque=0,partial=0,one_alpha=0;
    uint64_t hash=UINT64_C(14695981039346656037);
    for(int row=0;row<height;row++) for(int col=0;col<width;col++) {
        unsigned long pixel=XGetPixel(image,col,row);
        unsigned long alpha=(pixel>>picture->direct.alpha)&picture->direct.alphaMask;
        if(!alpha) clear++; else if(alpha==(unsigned short)picture->direct.alphaMask) opaque++; else partial++;
        if(alpha==1) one_alpha++;
        hash=(hash^pixel)*UINT64_C(1099511628211);
    }
    printf("{\"width\":%d,\"height\":%d,\"transparentPixels\":%lu,\"opaquePixels\":%lu,\"partialAlphaPixels\":%lu,\"oneAlphaPixels\":%lu,\"pixelHash\":\"%016llx\"}\n",width,height,clear,opaque,partial,one_alpha,(unsigned long long)hash);
    XDestroyImage(image); XFreePixmap(display,pixmap); XCloseDisplay(display); return 0;
}
