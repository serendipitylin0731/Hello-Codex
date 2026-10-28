//go:build windows

package main

import (
	"fmt"
	"syscall"
	"unsafe"
)

var kernel32 = syscall.NewLazyDLL("kernel32.dll")

func replaceFile(source, target string) error {
	// MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH avoids a crash gap.
	src, err := syscall.UTF16PtrFromString(source)
	if err != nil {
		return err
	}
	dst, err := syscall.UTF16PtrFromString(target)
	if err != nil {
		return err
	}
	r, _, callErr := kernel32.NewProc("MoveFileExW").Call(
		uintptr(unsafe.Pointer(src)),
		uintptr(unsafe.Pointer(dst)),
		uintptr(0x1|0x8),
	)
	if r == 0 {
		return fmt.Errorf("MoveFileExW: %v", callErr)
	}
	return nil
}

func singleInstanceLock() (func(), error) {
	name, err := syscall.UTF16PtrFromString(`Local\CodexQuotaPinger_V1`)
	if err != nil {
		return nil, err
	}
	// Keeping the named mutex handle open is enough for a single-instance guard.
	// Do not take ownership: Windows mutex ownership is tied to an OS thread, while
	// a Go goroutine may resume on a different thread before cleanup.
	r, _, callErr := kernel32.NewProc("CreateMutexW").Call(0, 0, uintptr(unsafe.Pointer(name)))
	if r == 0 {
		return nil, fmt.Errorf("CreateMutexW: %v", callErr)
	}
	closeHandle := func() { kernel32.NewProc("CloseHandle").Call(r) }
	if callErr == syscall.Errno(183) {
		closeHandle()
		return nil, fmt.Errorf("another CodexQuotaPinger is already running")
	}
	return closeHandle, nil
}
