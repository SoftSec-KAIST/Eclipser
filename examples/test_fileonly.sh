# Expected to find more than 8 new seeds with grey-box concolic testing.

gcc cmp.c -o cmp.bin -static -g || exit 1 # -static option for easier debugging
rm -rf box
mkdir box
cd box
dotnet ../../build/Eclipser.dll fuzz -p ../cmp.bin \
  --timelimit 5 --maxfilelen 16 --verbose 1 \
  --src file --fixfilepath input --initarg input
