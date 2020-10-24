#!/bin/bash

# Motivating example in the paper. Eclipser should find a crash immediately.
gcc motiv.c -o motiv.bin -static -g || exit 1
rm -rf box
mkdir box
cd box
dotnet ../../build/Eclipser.dll \
  -p ../motiv.bin -t 5 -v 2 -o output -f input --arg input
