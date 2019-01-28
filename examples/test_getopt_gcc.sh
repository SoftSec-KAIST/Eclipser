#!/bin/bash

# Grey-box concolic should find test cases "--number", "--squeeze", "--show".
gcc getopt.c -o getopt.bin -static -g || exit 1
rm -rf box
mkdir box
cd box
dotnet ../../build/Eclipser.dll fuzz -p ../getopt.bin -t 15 -v 1 \
  --src arg --maxarglen 10 
