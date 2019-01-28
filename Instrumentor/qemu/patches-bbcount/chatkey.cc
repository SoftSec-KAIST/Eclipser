#include <stdint.h>
#include <sys/mman.h>
#include <fcntl.h>
#include <assert.h>
#include <unistd.h>
#include <signal.h>
#include <string.h>
#include <sparsehash/dense_hash_set>

#include "config.h"

using google::dense_hash_set;

#ifdef TARGET_X86_64

typedef int64_t abi_long;
typedef uint64_t abi_ulong;
#define TARGET_NR_open          2
#define TARGET_NR_openat        257
#define TARGET_NR_mmap          9
#define TARGET_NR_mmap2         0xdeadbeaf // do not exist

#else

typedef int32_t abi_long;
typedef uint32_t abi_ulong;
#define TARGET_NR_open          5
#define TARGET_NR_openat        295
#define TARGET_NR_mmap          90
#define TARGET_NR_mmap2         192

#endif

typedef struct {
  char * filename;
  int fd;
} fd_info;

typedef struct {
  abi_ulong start_addr;
  abi_ulong end_addr;
  abi_ulong lib_hash;
  char * libname; // For debugging.
} lib_info;

#define BUFSIZE 4096
#define MAX_LIB_CNT 256

fd_info fd_arr[MAX_LIB_CNT];
lib_info lib_arr[MAX_LIB_CNT];
unsigned int lib_cnt = 0;

abi_ulong chatkey_entry_point; /* ELF entry point (_start) */
unsigned long chatkey_guest_base;

static abi_ulong path_hash = 5381; // djb2 hash


/* Global file pointers */
static FILE* coverage_fp;
static FILE* node_fp;
static FILE* edge_fp;
static FILE* path_fp;

static int is_fp_closed = 0;

// Holds nodes visited until now (accumulative).
static dense_hash_set<abi_ulong> accum_node_set;
// Holds edges visited until now (accumulative).
static dense_hash_set<abi_ulong> accum_edge_set;
// Holds path hashes observed until now (accumulative).
static dense_hash_set<abi_ulong> accum_path_set;
static int prev_node_cnt;
static int prev_edge_cnt;
static int prev_path_cnt;

/* Used in MODE_COUNTBB */
static abi_ulong prev_bb = 0;

/* Functions related to library information logging */

/* Calculate the hash value of library name, with djb2 hash */
static abi_ulong calculate_library_hash(char * libname) {
  char * ptr;
  abi_ulong hash = 5381; // djb2 hash
  for (ptr = libname; *ptr; ptr++) {
    hash = ((hash << 5) + hash) + (abi_ulong) (*ptr);
  }
  return hash;
}

/* Normalize (i.e. neutralize the randomization) the address of a basic block */
static abi_ulong normalize_bb(abi_ulong addr) {
  int i;
  abi_ulong normalized_addr = 0;

  for(i = 0; i < MAX_LIB_CNT && lib_arr[i].lib_hash; i++) {
    if (lib_arr[i].start_addr <= addr && addr <= lib_arr[i].end_addr) {
      //printf("addr %lx belongs to %s\n", addr, lib_arr[i].libname);
      normalized_addr = addr - lib_arr[i].start_addr + lib_arr[i].lib_hash;
      return normalized_addr;
    }
  }
  return addr;
}

static void update_file_descriptors(char * filename, int fd) {
  int i;

  // We do not care the cases where open() failed.
  if (fd == -1)
    return;

  for(i = 0; i < MAX_LIB_CNT; i++) {

    if(fd_arr[i].fd == fd) {
      // Overwrite this entry with the new filename
      strncpy(fd_arr[i].filename, filename, BUFSIZE);
      break;
    }

    if(fd_arr[i].filename == NULL) {
      // Found the first empty slot.
      fd_arr[i].fd = fd;
      fd_arr[i].filename = (char*) malloc(BUFSIZE);
      strncpy(fd_arr[i].filename, filename, BUFSIZE);
      break;
    }
  }
}

static void update_library(size_t size, int fd, abi_ulong start_addr) {
  int i;
  char * libname = NULL;
  abi_ulong lib_hash = 0;
  abi_ulong end_addr = 0;

  for(i = 0; i < MAX_LIB_CNT && fd_arr[i].filename; i++) {
    if(fd_arr[i].fd == fd) {
      // Found a matching file descriptor, so retrieve filname.
      libname = fd_arr[i].filename;
      break;
    }
  }

  if (libname == NULL)
    return;

  lib_hash = calculate_library_hash(libname);
  end_addr = start_addr + size;

  for(i = 0; i < MAX_LIB_CNT; i++) {
    if (lib_arr[i].lib_hash == lib_hash) {
      // Duplicate item found. Update address range only if the new mapping
      // subsumes the previous mapping. According to our observation on
      // executing dynamically linked binary with 'strace', this branch is
      // unlikely to be taken at all.
      if (start_addr <= lib_arr[i].start_addr &&
          end_addr >= lib_arr[i].end_addr)
      {
        printf("Updating the map!\n");
        lib_arr[i].start_addr = start_addr;
        lib_arr[i].end_addr = end_addr;
        break;
      }

    }

    if(lib_arr[i].lib_hash == 0) {
      // Found the first empty slot.
      lib_arr[i].libname = (char*) malloc(BUFSIZE);
      strncpy(lib_arr[i].libname, libname, BUFSIZE); // For debugging
      lib_arr[i].lib_hash = lib_hash;
      lib_arr[i].start_addr = start_addr;
      lib_arr[i].end_addr = end_addr;
      break;
    }
  }
}

extern "C" void chatkey_post_syscall(int num, abi_long arg1, abi_long arg2,
                                     abi_long arg5, abi_long ret) {
    char * addr;
    /* Note that we should log and analyze syscall even before chatkey_setup()
     * is called, since libraries are opened and mmapped before the entry point
     * of a binary is executed.
     */
    switch (num) {
        case TARGET_NR_open:
            addr = (char*) ((abi_ulong) arg1 + chatkey_guest_base);
            update_file_descriptors(addr, (int) ret);
            break;
        case TARGET_NR_openat:
            addr = (char*) ((abi_ulong) arg2 + chatkey_guest_base);
            update_file_descriptors(addr, (int) ret);
            break;
        case TARGET_NR_mmap:
        case TARGET_NR_mmap2:
            update_library((size_t) arg2, (int) arg5, (abi_ulong) ret);
            break;
        default:
            break;
    }

    return;
}

/* Functions related to basic block tracing */

static void init_accum_set(char* path, dense_hash_set<abi_ulong> *accum_set)
{
  int node_fd;
  off_t node_filesize;
  static abi_ulong* data;
  uint32_t cnt;

  node_fd = open(path, O_RDWR);
  assert(node_fd != -1);

  /* Read in elements from file and initialize 'accum_set' */
  node_filesize = lseek(node_fd, 0, SEEK_END);
  assert(node_filesize != -1);
  cnt = node_filesize / sizeof(abi_ulong);
  data = (abi_ulong *) mmap(NULL, node_filesize, PROT_READ | PROT_WRITE,
                                 MAP_SHARED, node_fd, 0);
  for (abi_ulong i = 0; i < cnt; i++)
    accum_set->insert(*(data + i));

  munmap(data, cnt * sizeof(abi_ulong));

  close(node_fd);

  return;
}

extern "C" void chatkey_setup(void) {

  /* Open file pointers and descriptors early, since if we try to open them in
   * chatkey_exit(), it gets mixed with stderr & stdout stream. This seems to
   * be an issue due to incorrect file descriptor management in QEMU code.
   */
  char * coverage_log = getenv("CK_COVERAGE_LOG");
  char * node_log = getenv("CK_NODE_LOG");
  char * edge_log = getenv("CK_EDGE_LOG");
  char * path_log = getenv("CK_PATH_LOG");

  assert(coverage_log != NULL);
  coverage_fp = fopen(coverage_log, "a");
  assert(coverage_fp != NULL);

  /* Caution : We cannot guarantee that timeout termination will be handled
   * correctly by signal handler. Therefore, open coverage_log in append mode
   * and log coverage accumulatively.
   */
  assert(node_log != NULL);
  assert(edge_log != NULL);
  assert(path_log != NULL);

  node_fp = fopen(node_log, "a+");
  edge_fp = fopen(edge_log, "a+");
  path_fp = fopen(path_log, "a+");

  assert(node_fp != NULL);
  assert(edge_fp != NULL);
  assert(path_fp != NULL);

  accum_node_set.set_empty_key(0);
  accum_edge_set.set_empty_key(0);
  accum_path_set.set_empty_key(0);

  init_accum_set(node_log, &accum_node_set);
  init_accum_set(edge_log, &accum_edge_set);
  init_accum_set(path_log, &accum_path_set);

  prev_node_cnt = accum_node_set.size();
  prev_edge_cnt = accum_edge_set.size();
  prev_path_cnt = accum_path_set.size();

}

// When fork() syscall is encountered, child process should call this function
extern "C" void chatkey_close_fp(void) {

  is_fp_closed = 1;

  fclose(coverage_fp);
  fclose(node_fp);
  fclose(edge_fp);
  fclose(path_fp);

}

extern "C" void chatkey_exit(void) {
  sigset_t mask;
  unsigned int accum_node_cnt, accum_edge_cnt, accum_path_cnt;
  unsigned int new_node_cnt, new_edge_cnt, new_path_cnt;

  // If chatkey_close_fp() was called, then return without any action.
  if (is_fp_closed)
    return;

  // Block signals, since we register signal handler that calls chatkey_exit()/
  if (sigfillset(&mask) < 0)
    return;
  if (sigprocmask(SIG_BLOCK, &mask, NULL) < 0)
    return;


  // Add path hash to the set if it's a previously unseen one.
  if (accum_path_set.find(path_hash) == accum_path_set.end()) {
    accum_path_set.insert(path_hash);
    fwrite(&path_hash, sizeof(abi_ulong), 1, path_fp);
  }

  accum_node_cnt = accum_node_set.size();
  accum_edge_cnt = accum_edge_set.size();
  accum_path_cnt = accum_path_set.size();
  new_node_cnt = accum_node_cnt - prev_node_cnt;
  new_edge_cnt = accum_edge_cnt - prev_edge_cnt;
  new_path_cnt = accum_path_cnt - prev_path_cnt;

  fprintf(coverage_fp, "Visited nodes : %d (+%d)\n", \
                       accum_node_cnt, new_node_cnt);
  fprintf(coverage_fp, "Visited edges : %d (+%d)\n", \
                       accum_edge_cnt, new_edge_cnt);
  fprintf(coverage_fp, "Explolred paths : %d (+%d)\n", \
                       accum_path_cnt, new_path_cnt);
  fprintf(coverage_fp, "=========================\n");

  fclose(coverage_fp);
  fclose(node_fp);
  fclose(edge_fp);
  fclose(path_fp);
}

extern "C" void chatkey_log_bb(abi_ulong node) {
    abi_ulong edge;

    /* If coverage_fp is NULL, it means that chatkey_setup() is not called yet
     * This happens when QEMU is executing a dynamically linked program.
     */
    if (!coverage_fp)
      return;

    node = normalize_bb(node);

    /* Hack to convert a pair of BB into a hash value that represents edge */
#ifdef TARGET_X86_64
    edge = (prev_bb << 16) ^ node;
#else
    edge = (prev_bb << 8) ^ node;
#endif

    /* Note that we dump the set immediately back to file, since we cannot
     * guarantee that timeout termination can be handled by signal handler.
     */
    if (accum_node_set.find(node) == accum_node_set.end()) {
      accum_node_set.insert(node);
      fwrite(&node, sizeof(abi_ulong), 1, node_fp);
    }

    if (accum_edge_set.find(edge) == accum_edge_set.end()) {
      accum_edge_set.insert(edge);
      fwrite(&edge, sizeof(abi_ulong), 1, edge_fp);
    }
    prev_bb = node;
}

extern "C" void chatkey_update_path_hash(register abi_ulong addr) {
    register unsigned int i;
    for (i = 0; i < sizeof(abi_ulong); i++)
        path_hash = ((path_hash << 5) + path_hash) + ((addr >> (i<<3)) & 0xff);
}
