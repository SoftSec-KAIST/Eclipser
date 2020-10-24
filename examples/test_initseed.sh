#!/bin/bash

# Tests if Eclipser can solve simple equality checking branch conditions when
# initial seeds are given.
gcc linear.c -o linear.bin -static -g || exit 1
rm -rf box
mkdir box
cd box
mkdir seeds
python -c 'print "B" * 16' > seeds/input
dotnet ../../build/Eclipser.dll \
  -p ../linear.bin -t 5 -v 2 -i seeds -o output -f input --arg input
