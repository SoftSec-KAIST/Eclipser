#include <stdio.h>
#include <getopt.h>
#include <stdlib.h>
#include <string.h>

int main(int argc, char ** argv){
  char c;
  static struct option const long_options[] =
  {
    {"number", no_argument, NULL, 'n'},
    {"squeeze", no_argument, NULL, 's'},
    {"show", no_argument, NULL, 'A'},
    {NULL, 0, NULL, 0}
  };

  while ((c = getopt_long (argc, argv, "nsA", long_options, NULL)) != -1)
  {
    switch (c)
    {
      case 'n':
        printf("Case 1\n"); // new path 2
        break;

      case 's':
        printf("Case 2\n"); // new path 3
        break;

      case 'A':
        printf("Case 3\n"); // new path 4
        break;

      default:
        printf("failed, usage...\n");
    }
  }

  return 0;
}
