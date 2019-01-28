#include <stdint.h>
#include <stdlib.h>
#include <stdio.h>
#include <signal.h>

#include "qemu.h"

#ifdef TARGET_X86_64
typedef int64_t abi_long;
typedef uint64_t abi_ulong;

#define TARGET_NR_open          2
#define TARGET_NR_openat        257
#define TARGET_NR_read          0
#define TARGET_NR_dup           32
#define TARGET_NR_dup2          33
#define TARGET_NR_dup3          292
#define TARGET_NR_bind          49
#define TARGET_NR_exit_group    231
#else
typedef int32_t abi_long;
typedef uint32_t abi_ulong;

#define TARGET_NR_open          5
#define TARGET_NR_openat        295
#define TARGET_NR_read          3
#define TARGET_NR_dup           41
#define TARGET_NR_dup2          63
#define TARGET_NR_dup3          330
#define TARGET_NR_bind          0xdeadbeef
#define TARGET_NR_exit_group    252
#endif

#define BUFSIZE 4096

abi_ulong chatkey_entry_point;

static FILE *log_fp = NULL;

void chatkey_setup(void);
void chatkey_close_fp(void);
void chatkey_exit(void);
void chatkey_pre_syscall(int num, abi_long arg1, abi_long arg2);
void chatkey_post_syscall(int num, abi_long arg1, abi_long arg2, abi_long ret);
void escape_white_space(char * dst, char * src, int size);

void escape_white_space(char * dst, char * src, int size) {
  char * ptr_dst = dst;
  char * ptr_src = src;

  while(*ptr_src && ptr_dst < dst + size - 4) {
    if (*ptr_src == '\n') {
      ptr_src++;
      *ptr_dst++ = '\\';
      *ptr_dst++ = 'n';
    } else if (*ptr_src == '\r') {
      ptr_src++;
      *ptr_dst++ = '\\';
      *ptr_dst++ = 'r';
    } else if (*ptr_src == ' ') {
      ptr_src++;
      *ptr_dst++ = '\\';
      *ptr_dst++ = 's';
    } else if (*ptr_src == '\t') {
      ptr_src++;
      *ptr_dst++ = '\\';
      *ptr_dst++ = 't';
    } else {
      *ptr_dst++ = *ptr_src++;
    }
  }
  *ptr_dst = '\0';
}

void chatkey_setup(void) {
  char *log_path = getenv("CK_SYSCALL_LOG");
  if (!log_path || log_fp)
      return;
  log_fp = fopen(log_path, "w");
}

// When fork() syscall is encountered, child process should call this function
void chatkey_close_fp(void) {
  // close 'log_fp', since we don't want to dump log twice
  fclose(log_fp);
  log_fp = NULL;
}

void chatkey_exit(void) {
  sigset_t mask;

  // Block signals, since we register signal handler that calls chatkey_exit()
  if (sigfillset(&mask) < 0)
    return;
  if (sigprocmask(SIG_BLOCK, &mask, NULL) < 0)
    return;

  if (log_fp != NULL)
    fclose(log_fp);
}

void chatkey_pre_syscall(int num, abi_long arg1, abi_long arg2) {
    if (!log_fp)
        return;

    switch (num) {
        case TARGET_NR_read:
            fprintf(log_fp, "read %d\n", (int) arg1);
            break;
        case TARGET_NR_dup2:
        case TARGET_NR_dup3:
            fprintf(log_fp, "dup %d %d\n", (int) arg1, (int) arg2);
            break;
        default:
            break;
    }
}

void chatkey_post_syscall(int num, abi_long arg1, abi_long arg2, abi_long ret) {
    char filename[BUFSIZE];
    char* addr;

    /* If log_fp is NULL, it means that chatkey_setup() is not called yet. This
     * happens when QEMU is executing a dynamically linked program.
     */
    if (!log_fp)
        return;

    switch (num) {
        case TARGET_NR_open:
            addr = (char*) ((abi_ulong) arg1 + guest_base);
            escape_white_space(filename, addr, BUFSIZE);
            fprintf(log_fp, "open %d %s\n", (int) ret, filename);
            break;
        case TARGET_NR_openat:
            addr = (char*) ((abi_ulong) arg2 + guest_base);
            escape_white_space(filename, addr, BUFSIZE);
            fprintf(log_fp, "open %d %s\n", (int) ret, filename);
            break;
        case TARGET_NR_dup:
            fprintf(log_fp, "dup %d %d\n", (int) arg1, (int) ret);
            break;
        default:
            break;
    }
}
