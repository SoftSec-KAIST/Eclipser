/* A variatn of cmp.c example, to test if Eclipser can handle programs that
 * raises segfault.
 */

#include <stdio.h>
#include <unistd.h>
#include <sys/types.h>
#include <sys/stat.h>
#include <fcntl.h>

int main(int argc, char ** argv){
  char c;
  short s;
  int i;
  int64_t i64;
  int fd;

  if (argc < 2)
    return -1;

  fd = open(argv[1], O_RDWR);

  read(fd, &i, sizeof(int));
  if (i == 0x41424344) {
    printf("Found new path!\n");
    * (int *) i = 0;
  }

  close(fd);

  return 0;
}
