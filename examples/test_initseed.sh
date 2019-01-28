# Expected to find more than 9 new seeds with grey-box concolic testing.

gcc cmp.c -o cmp.bin -static -g || exit 1 # -static option for easier debugging
rm -rf box
mkdir box
cd box
mkdir seeds
python -c 'print "B" * 16' > seeds/input
dotnet ../../build/Eclipser.dll fuzz -p ../cmp.bin -t 5 -v 1 \
  --src file --maxfilelen 17 \
  --initseedsdir seeds --fixfilepath input --initarg input
