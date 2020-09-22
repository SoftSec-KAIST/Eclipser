#include <stdint.h>
#include <stdbool.h>
#include <stdlib.h>
#include <stdio.h>
#include <assert.h>
#include <unistd.h>
#include <signal.h>

#include "tcg.h"

#ifdef TARGET_X86_64
typedef uint64_t abi_ulong;
#else
typedef uint32_t abi_ulong;
#endif

extern unsigned int afl_forksrv_pid;
#define FORKSRV_FD 198
#define TSL_FD (FORKSRV_FD - 1)

#define MAX_TRACE_LEN (1000000)

static abi_ulong hash = 5381; // djb2 hash

abi_ulong chatkey_entry_point;
abi_ulong chatkey_curr_addr;
abi_ulong chatkey_targ_addr;
unsigned char trace_buffer[MAX_TRACE_LEN * (sizeof(abi_ulong) + sizeof(unsigned char) + 2 * sizeof(abi_ulong)) + 64];
unsigned char * buf_ptr = trace_buffer;

size_t chatkey_targ_index;
bool chatkey_EP_passed = false;

static size_t targ_hit_count = 0;
static size_t trace_count = 0;
static FILE * branch_fp;
static FILE * hash_fp;

void flush_trace_buffer(void);
void chatkey_setup(void);
void chatkey_close_fp(void);
void chatkey_exit(void);
void chatkey_log_branch(abi_ulong oprnd1, abi_ulong oprnd2, unsigned char operand_type);
void chatkey_update_hash(register abi_ulong addr);

void flush_trace_buffer(void) {
  size_t len = buf_ptr - trace_buffer;
  fwrite(trace_buffer, len, 1, branch_fp);
}

void chatkey_setup(void) {

  char * branch_path = getenv("CK_FEED_LOG");
  char * hash_path = getenv("CK_HASH_LOG");

  assert(branch_path != NULL);
  branch_fp = fopen(branch_path, "w");
  assert(branch_fp != NULL);

  assert(hash_path != NULL);
  hash_fp = fopen(hash_path, "w");
  assert(hash_fp != NULL);

  assert(getenv("CK_FORK_SERVER") != NULL);
  // If fork server is enabled, chatkey_targ_* should have been set already.
  if (atoi(getenv("CK_FORK_SERVER")) == 0) {
    chatkey_targ_addr = strtol(getenv("CK_FEED_ADDR"), NULL, 16);
    chatkey_targ_index = strtol(getenv("CK_FEED_IDX"), NULL, 16);
  }

}

// When fork() syscall is encountered, child process should call this function
void chatkey_close_fp(void) {

  // close 'branch_fp', since we don't want to dump log twice
  fclose(branch_fp);
  branch_fp = NULL;

  fclose(hash_fp);

  if (afl_forksrv_pid)
    close(TSL_FD);
}

void chatkey_exit(void) {
  abi_ulong nil = 0;
  sigset_t mask;

  // If chatkey_close_fp() was called, then return without any action
  if (branch_fp == NULL)
    return;

  // Block signals, since we register signal handler that calls chatkey_exit()
  if (sigfillset(&mask) < 0)
    return;
  if (sigprocmask(SIG_BLOCK, &mask, NULL) < 0)
    return;

  flush_trace_buffer();

  fwrite(&nil, sizeof(abi_ulong), 1, branch_fp);
  fclose(branch_fp);

#ifdef TARGET_X86_64
  fprintf(hash_fp, "%lu\n", hash);
#else
  fprintf(hash_fp, "%u\n", hash);
#endif
  fclose(hash_fp);
}

/* Recall that in 64bit we already pushed rdi/rsi/rdx before calling
 * chatkey_trampline().
 */
asm (".global chatkey_trampoline                        \t\n\
      .type chatkey_trampoline, @function               \t\n\
      chatkey_trampoline:                               \t\n\
      push %rax                                         \t\n\
      push %rcx                                         \t\n\
      push %r8                                          \t\n\
      push %r9                                          \t\n\
      push %r10                                         \t\n\
      push %r11                                         \t\n\
      call chatkey_log_branch;                        \t\n\
      pop %r11                                          \t\n\
      pop %r10                                          \t\n\
      pop %r9                                           \t\n\
      pop %r8                                           \t\n\
      pop %rcx                                          \t\n\
      pop %rax                                          \t\n\
      ret                                               \t\n\
      .size chatkey_trampoline, . - chatkey_trampoline  \t\n\
      ");

void chatkey_log_branch(abi_ulong oprnd1, abi_ulong oprnd2, unsigned char type) {

  abi_ulong oprnd1_truncated, oprnd2_truncated;
  unsigned char operand_type = type & 0x3f;
  unsigned char compare_type = type & 0xc0;
  unsigned char operand_size;

  if (!branch_fp)
    return;

  if (chatkey_targ_addr) {
    /* We're in the mode that traces cmp/test at a specific address */
    if (chatkey_curr_addr == chatkey_targ_addr &&
        ++targ_hit_count == chatkey_targ_index) { // Note that index starts from 1.
      if (operand_type == MO_8) {
        oprnd1_truncated = oprnd1 & 0xff;
        oprnd2_truncated = oprnd2 & 0xff;
        operand_size = 1;
      } else if (operand_type == MO_16) {
        oprnd1_truncated = oprnd1 & 0xffff;
        oprnd2_truncated = oprnd2 & 0xffff;
        operand_size = 2;
      }
#ifdef TARGET_X86_64
      else if (operand_type == MO_32) {
        oprnd1_truncated = oprnd1 & 0xffffffff;
        oprnd2_truncated = oprnd2 & 0xffffffff;
        operand_size = 4;
      } else if (operand_type == MO_64) {
        oprnd1_truncated = oprnd1;
        oprnd2_truncated = oprnd2;
        operand_size = 8;
      }
#else
      else if (operand_type == MO_32) {
        oprnd1_truncated = oprnd1;
        oprnd2_truncated = oprnd2;
        operand_size = 4;
      }
#endif
      else {
        assert(false);
      }

      type = compare_type | operand_size;
      fwrite(&chatkey_curr_addr, sizeof(abi_ulong), 1, branch_fp);
      fwrite(&type, sizeof(unsigned char), 1, branch_fp);
      fwrite(&oprnd1_truncated, operand_size, 1, branch_fp);
      fwrite(&oprnd2_truncated, operand_size, 1, branch_fp);
      if (oprnd1_truncated != oprnd2_truncated) {
        /* If two operands are not equal, then path hash of this execution is
         * not used in Chatkey. Therefore, finish execution to save time.
         */
        chatkey_exit();
        exit(0);
      }
    }
  } else if (trace_count ++ < MAX_TRACE_LEN) {
    /* We're in the mode that traces all the cmp/test instructions */
    if (operand_type == MO_8) {
      oprnd1_truncated = oprnd1 & 0xff;
      oprnd2_truncated = oprnd2 & 0xff;
      operand_size = 1;
      * (buf_ptr + sizeof(abi_ulong) + sizeof(unsigned char)) = oprnd1_truncated;
      * (buf_ptr + sizeof(abi_ulong) + sizeof(unsigned char) + operand_size) = oprnd2_truncated;
    } else if (operand_type == MO_16) {
      oprnd1_truncated = oprnd1 & 0xffff;
      oprnd2_truncated = oprnd2 & 0xffff;
      operand_size = 2;
      * (unsigned short*) (buf_ptr + sizeof(abi_ulong) + sizeof(unsigned char)) = oprnd1_truncated;
      * (unsigned short*) (buf_ptr + sizeof(abi_ulong) + sizeof(unsigned char) + operand_size) = oprnd2_truncated;
    }
#ifdef TARGET_X86_64
    else if (operand_type == MO_32) {
      oprnd1_truncated = oprnd1 & 0xffffffff;
      oprnd2_truncated = oprnd2 & 0xffffffff;
      operand_size = 4;
      * (unsigned int*) (buf_ptr + sizeof(abi_ulong) + sizeof(unsigned char)) = oprnd1_truncated;
      * (unsigned int*) (buf_ptr + sizeof(abi_ulong) + sizeof(unsigned char) + operand_size) = oprnd2_truncated;
    } else if (operand_type == MO_64) {
      oprnd1_truncated = oprnd1;
      oprnd2_truncated = oprnd2;
      operand_size = 8;
      * (uint64_t*) (buf_ptr + sizeof(abi_ulong) + sizeof(unsigned char)) = oprnd1_truncated;
      * (uint64_t*) (buf_ptr + sizeof(abi_ulong) + sizeof(unsigned char) + operand_size) = oprnd2_truncated;
    }
#else
    else if (operand_type == MO_32) {
      oprnd1_truncated = oprnd1;
      oprnd2_truncated = oprnd2;
      operand_size = 4;
      * (unsigned int*) (buf_ptr + sizeof(abi_ulong) + sizeof(unsigned char)) = oprnd1_truncated;
      * (unsigned int*) (buf_ptr + sizeof(abi_ulong) + sizeof(unsigned char) + operand_size) = oprnd2_truncated;
    }
#endif
    else {
      assert(false);
    }

    type = compare_type | operand_size;

    * (abi_ulong*) buf_ptr = chatkey_curr_addr;
    * (buf_ptr + sizeof(abi_ulong)) = type;
    buf_ptr += sizeof(abi_ulong) + sizeof(unsigned char) + 2 * operand_size;

  } else {
    /* We're in the mode that traces all the cmp/test instructions, and trace
     * limit has exceeded. Abort tracing. */
    abi_ulong nil = 0;
    flush_trace_buffer();
    fwrite(&nil, sizeof(abi_ulong), 1, branch_fp);
    fclose(branch_fp);
    // output 0 as path hash to indicate abortion.
    fprintf(hash_fp, "0\n");
    fclose(hash_fp);
    exit(0);
  }
}


void chatkey_update_hash(register abi_ulong addr) {
    register unsigned int i;
    for (i = 0; i < sizeof(abi_ulong); i++)
        hash = ((hash << 5) + hash) + ((addr >> (i<<3)) & 0xff);
}
