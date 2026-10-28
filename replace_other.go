//go:build !windows

package main

import (
	"net"
	"os"
)

func replaceFile(source, target string) error {
	return os.Rename(source, target)
}

func singleInstanceLock() (func(), error) {
	listener, err := net.Listen("tcp", "127.0.0.1:49763")
	if err != nil {
		return nil, err
	}
	return func() { _ = listener.Close() }, nil
}
