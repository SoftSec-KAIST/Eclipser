/* A simple example to test whether Eclipser can handle strcmp() calls. */

#include <stdio.h>
#include <getopt.h>
#include <stdlib.h>
#include <string.h>

int main(int argc, char ** argv) {
  char c;
  short s;
  int i;
  int64_t i64;

  if (argc < 2)
    return -1;

  if (strcmp(argv[1], "--option") == 0)
    printf("Found new path 1!\n");

  /* Note that this may fail in 32bit environment, since lowest 1-byte field of
   * %esi, %dsi, %ebp, %esp are not accessible (%sil, %dil, %bpl, %spl are only
   * accessible in 64bit environment, by using REX prefix). Therefore, our
   * instrumentation code added in QEMU may not be able to generate 1-byte
   * subtraction opcode, which results in the failure of flag-search on string.
   */
  c = (char) strcmp(argv[1], "--char");
  if (c == 0)
    printf("Found new path 2!\n");

  s = (short) strcmp(argv[1], "--short");
  if (s == 0)
    printf("Found new path 3!\n");

  i = strcmp(argv[1], "--int32");
  if (i == 0)
    printf("Found new path 4!\n");

  i64 = strcmp(argv[1], "--int64");
  if (i64 == 0ll)
    printf("Found new path 5!\n");

  return 0;
}
