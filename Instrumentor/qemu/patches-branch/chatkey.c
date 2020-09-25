#include <stdint.h>
#include <stdlib.h>
#include <stdio.h>
#include <assert.h>
#include <unistd.h>
#include <signal.h>
#include <sys/mman.h>
#include <fcntl.h>
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

abi_ulong chatkey_entry_point = 0; /* ELF entry point (_start) */
abi_ulong chatkey_curr_addr = 0;
abi_ulong chatkey_targ_addr = 0;
uint32_t chatkey_targ_index = 0;
int measure_coverage = 0;
int chatkey_EP_passed = 0;

static int found_new_edge = 0;
static int found_new_path = 0; // TODO. Extend to measure path coverage, too.
static abi_ulong prev_node = 0;
static char * coverage_path = NULL;
static char * branch_path = NULL;
static FILE * coverage_fp = NULL;
static FILE * branch_fp = NULL;
static unsigned char * edge_bitmap = NULL;

unsigned char trace_buffer[MAX_TRACE_LEN * (sizeof(abi_ulong) + sizeof(unsigned char) + 2 * sizeof(abi_ulong)) + 64];
unsigned char * buf_ptr = trace_buffer;

static uint32_t targ_hit_count = 0;
static uint32_t trace_count = 0;

void flush_trace_buffer(void) {
  size_t len = buf_ptr - trace_buffer;
  fwrite(trace_buffer, len, 1, branch_fp);
}

void chatkey_setup_before_forkserver(void) {
  char * bitmap_path = getenv("CK_BITMAP_LOG");
  int bitmap_fd = open(bitmap_path, O_RDWR | O_CREAT, 0644);
  edge_bitmap = (unsigned char*) mmap(NULL, 0x10000, PROT_READ | PROT_WRITE, MAP_SHARED, bitmap_fd, 0);
  assert(edge_bitmap != (void *) -1);

  coverage_path = getenv("CK_COVERAGE_LOG");
  branch_path = getenv("CK_FEED_LOG");

  chatkey_EP_passed = 1;
}

void chatkey_setup_after_forkserver(void) {

  assert(getenv("CK_FORK_SERVER") != NULL);
  // If fork server is enabled, the following data are set during the handshake.
  if (atoi(getenv("CK_FORK_SERVER")) == 0) {
    chatkey_targ_addr = strtol(getenv("CK_FEED_ADDR"), NULL, 16);
    chatkey_targ_index = strtol(getenv("CK_FEED_IDX"), NULL, 16);
    measure_coverage = atoi(getenv("CK_MEASURE_COV"));
  }

  if (measure_coverage == 1) {
    coverage_fp = fopen(coverage_path, "w");
    assert(coverage_fp != NULL);
  }

  branch_fp = fopen(branch_path, "w");
  assert(branch_fp != NULL);
}

// When fork() syscall is encountered, child process should call this function.
void chatkey_close_fp(void) {
  // Close file pointers, to avoid dumping log twice.
  if (coverage_fp) {
    fclose(coverage_fp);
    coverage_fp = NULL;
  }

  if (branch_fp) {
    fclose(branch_fp);
    branch_fp = NULL;
  }

  if (afl_forksrv_pid)
    close(TSL_FD);
}

void chatkey_exit(void) {
  abi_ulong nil = 0;
  sigset_t mask;

  // Block signals, since we register signal handler that calls chatkey_exit()
  if (sigfillset(&mask) < 0)
    return;
  if (sigprocmask(SIG_BLOCK, &mask, NULL) < 0)
    return;

  if (coverage_fp) {
    fprintf(coverage_fp, "%d\n%d\n", found_new_edge, found_new_path);
    fclose(coverage_fp);
    coverage_fp = NULL;
  }

  if (branch_fp) {
    flush_trace_buffer();
    fwrite(&nil, sizeof(abi_ulong), 1, branch_fp);
    fclose(branch_fp);
    branch_fp = NULL;
  }

  if (edge_bitmap) {
    munmap(edge_bitmap, 0x10000);
    edge_bitmap = NULL;
  }
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
      if (oprnd1_truncated != oprnd2_truncated || !coverage_fp) {
        /* If the two operands are not equal, exit signal or coverage gain is
         * not used in F# code. Simiarly, when coverage_fp is NULL, this means
         * we are interested in branch distance only, and not in exit signal or
         * coverage gain. In these case, halt the execution here to save time.
         */
        chatkey_exit();
        exit(0);
      }
    }
  } else if (trace_count++ < MAX_TRACE_LEN) {
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
    chatkey_exit();
    exit(0);
  }
}

void chatkey_log_bb(abi_ulong addr) {
  abi_ulong edge, hash;
  unsigned int byte_idx, byte_mask;
  unsigned char old_byte, new_byte;

  chatkey_curr_addr = addr;

  if (!coverage_fp || !edge_bitmap)
    return;

#ifdef TARGET_X86_64
  edge = (prev_node << 16) ^ addr;
#else
  edge = (prev_node << 8) ^ addr;
#endif
  prev_node = addr;

  // Update bitmap.
  hash = (edge >> 4) ^ (edge << 8);
  byte_idx = (hash >> 3) & 0xffff;
  byte_mask = 1 << (hash & 0x7); // Use the lowest 3 bits to shift
  old_byte = edge_bitmap[byte_idx];
  new_byte = old_byte | byte_mask;
  if (old_byte != new_byte) {
    found_new_edge = 1;
    edge_bitmap[byte_idx] = new_byte;
  }
}
