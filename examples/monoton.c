/* Ab example with custom strcmp() function that requires binary search. */

#include <stdio.h>
#include <unistd.h>
#include <sys/stat.h>
#include <fcntl.h>

int my_strcmp(const char *s1, const char *s2)
{
      for ( ; *s1 == *s2; s1++, s2++)
        if (*s1 == '\0')
          return 0;
        return ((*(unsigned char *)s1 < *(unsigned char *)s2) ? -1 : +1);
}

int main(int argc, char ** argv) {
  unsigned int i;
  char buf[9];
  size_t n;
  int fd;

  if (argc < 2)
    return -1;

  fd = open(argv[1], O_RDWR);

  read(fd, &i, sizeof(i));

  if (i * i == 0x10A29504) // 0x4142 ^ 2
    printf("Found new path 1\n");

  n = read(fd, buf, 8);
  buf[n] = '\0';

  if (my_strcmp(buf, "Good!") == 0)
    printf("Found new path\n");

  return 0;
}
