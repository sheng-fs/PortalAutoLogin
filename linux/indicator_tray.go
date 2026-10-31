//go:build tray

package main

/*
#cgo pkg-config: gtk+-3.0 ayatana-appindicator3-0.1
#include <stdlib.h>
#include <libayatana-appindicator/app-indicator.h>

static void set_indicator_menu(AppIndicator *indicator, GtkWidget *menu) {
    app_indicator_set_menu(indicator, GTK_MENU(menu));
}

static void set_indicator_icon(AppIndicator *indicator, const char *name) {
    app_indicator_set_icon_full(indicator, name, name);
}

static void set_indicator_label(AppIndicator *indicator, const char *text) {
    app_indicator_set_label(indicator, text, text);
}
*/
import "C"

import (
	"unsafe"

	"github.com/gotk3/gotk3/gtk"
)

type trayIndicator struct {
	ptr *C.AppIndicator
}

func newTrayIndicator(title string) *trayIndicator {
	id := C.CString("portalautologin")
	icon := C.CString("portalautologin")
	defer C.free(unsafe.Pointer(id))
	defer C.free(unsafe.Pointer(icon))

	ptr := C.app_indicator_new(id, icon, C.APP_INDICATOR_CATEGORY_APPLICATION_STATUS)
	C.app_indicator_set_status(ptr, C.APP_INDICATOR_STATUS_ACTIVE)
	cTitle := C.CString(title)
	C.app_indicator_set_title(ptr, cTitle)
	C.free(unsafe.Pointer(cTitle))

	return &trayIndicator{ptr: ptr}
}

func (t *trayIndicator) SetMenu(menu *gtk.Menu) {
	C.set_indicator_menu(t.ptr, (*C.GtkWidget)(unsafe.Pointer(menu.Native())))
}

func (t *trayIndicator) SetIcon(name string) {
	cName := C.CString(name)
	defer C.free(unsafe.Pointer(cName))
	C.set_indicator_icon(t.ptr, cName)
}

func (t *trayIndicator) SetLabel(text string) {
	cText := C.CString(text)
	defer C.free(unsafe.Pointer(cText))
	C.set_indicator_label(t.ptr, cText)
}

func (t *trayIndicator) SetActive(active bool) {
	var status C.AppIndicatorStatus = C.APP_INDICATOR_STATUS_PASSIVE
	if active {
		status = C.APP_INDICATOR_STATUS_ACTIVE
	}
	C.app_indicator_set_status(t.ptr, status)
}
