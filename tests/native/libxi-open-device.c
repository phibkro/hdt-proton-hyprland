/* SPDX-License-Identifier: MIT
 * Project-owned regression: XOpenDevice must retain its requested device ID
 * while another thread reuses the same display output buffer. No input injected.
 * Run only on the private X server owned by libxi-open-device.sh.
 */
#include <X11/Xlib.h>
#include <X11/extensions/XInput.h>
#include <pthread.h>
#include <stdatomic.h>
#include <stdio.h>
#include <stdlib.h>
static Display *display;
static atomic_int stop;
static void *flood(void *unused)
{
    (void)unused;
    while (!atomic_load(&stop))
    {
        for (int i = 0; i < 256; i++)
            XNoOp(display);
        XFlush(display);
    }
    return NULL;
}
int main(int argc, char **argv)
{
    if (argc != 2 || !getenv("HDT_LIBXI_REPRO") || !XInitThreads())
        return 64;
    display = XOpenDisplay(argv[1]);
    if (!display)
        return 65;
    int count = 0, id = 0;
    XDeviceInfo *devices = XListInputDevices(display, &count);
    for (int i = 0; i < count; i++)
        if (devices[i].use == IsXExtensionPointer)
        {
            id = (int)devices[i].id;
            break;
        }
    XFreeDeviceList(devices);
    if (!id)
        return 66;
    XDevice *device = XOpenDevice(display, id);
    if (!device)
        return 67;
    XCloseDevice(display, device);
    pthread_t thread;
    if (pthread_create(&thread, NULL, flood, NULL))
        return 68;
    int mismatch = 0;
    int completed = 0;
    for (int i = 0; i < 50000; i++)
    {
        device = XOpenDevice(display, id);
        if (!device)
        {
            mismatch = 2;
            break;
        }
        if (device->device_id != (XID)id)
        {
            printf("MISMATCH iteration=%d requested=%d returned=%lu\n", i, id, device->device_id);
            mismatch = 1;
            device->device_id = (XID)id;
        }
        if (!mismatch)
        {
            unsigned char map[256];
            if (XGetDeviceButtonMapping(display, device, map, sizeof(map)) <= 0)
                mismatch = 3;
        }
        XCloseDevice(display, device);
        if (!mismatch)
            completed++;
        if (mismatch)
            break;
    }
    atomic_store(&stop, 1);
    pthread_join(thread, NULL);
    XCloseDisplay(display);
    printf("completed=%d mismatch=%d\n", completed, mismatch);
    return mismatch == 2 ? 69 : mismatch == 3 ? 70 : mismatch;
}
