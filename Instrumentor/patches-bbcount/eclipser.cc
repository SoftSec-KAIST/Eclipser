#include <stdint.h>
#include <stdlib.h>
#include <stdio.h>
#include <assert.h>
#include <unistd.h>
#include <signal.h>
#include <sys/mman.h>
#include <fcntl.h>
#include <set>

using namespace std;

// It turns out to be hard to import TARGET_X86_64 macro in C++ source, so just
// use uint64_t as abi_ulong, regardless of architecture.
typedef uint64_t abi_ulong;


static void init_accum_set(char* path, set<abi_ulong> *accum_set);
extern "C" void eclipser_setup(void);
extern "C" void eclipser_detach(void);
extern "C" void eclipser_exit(void);
extern "C" void eclipser_log_bb(abi_ulong node);

/* ELF entry point (_start). */
abi_ulong eclipser_entry_point;

static FILE* coverage_fp;
static FILE* node_fp;
static FILE* edge_fp;

static abi_ulong prev_node = 0;
static set<abi_ulong> accum_node_set;
static set<abi_ulong> accum_edge_set;
static int last_node_cnt;
static int last_edge_cnt;

/* Functions related to basic block tracing */

static void init_accum_set(char* path, set<abi_ulong> *accum_set) {
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

extern "C" void eclipser_setup(void) {

  /* Open file pointers and descriptors early, since if we try to open them in
   * eclipser_exit(), it gets mixed with stderr & stdout stream. This seems to
   * be an issue due to incorrect file descriptor management in QEMU code.
   */
  char * coverage_log = getenv("ECL_COVERAGE_LOG");
  char * node_log = getenv("ECL_NODE_LOG");
  char * edge_log = getenv("ECL_EDGE_LOG");

  assert(coverage_log != NULL);
  coverage_fp = fopen(coverage_log, "a");
  assert(coverage_fp != NULL);

  /* Caution : We cannot guarantee that timeout termination will be handled
   * correctly by signal handler. Therefore, open coverage_log in append mode
   * and log coverage accumulatively.
   */
  assert(node_log != NULL);
  assert(edge_log != NULL);

  node_fp = fopen(node_log, "a+");
  edge_fp = fopen(edge_log, "a+");

  assert(node_fp != NULL);
  assert(edge_fp != NULL);

  init_accum_set(node_log, &accum_node_set);
  init_accum_set(edge_log, &accum_edge_set);

  last_node_cnt = accum_node_set.size();
  last_edge_cnt = accum_edge_set.size();

}

// When fork() syscall is encountered, child process should call this function
// to detach from Eclipser.
extern "C" void eclipser_detach(void) {
  if (coverage_fp) {
    fclose(coverage_fp);
    coverage_fp = NULL;
  }

  if (node_fp) {
    fclose(node_fp);
    node_fp = NULL;
  }

  if (edge_fp) {
    fclose(edge_fp);
    edge_fp = NULL;
  }
}

extern "C" void eclipser_exit(void) {
  sigset_t mask;
  unsigned int accum_node_cnt, accum_edge_cnt;
  unsigned int new_node_cnt, new_edge_cnt;

  // Block signals, since we register signal handler that calls eclipser_exit().
  if (sigfillset(&mask) < 0)
    return;
  if (sigprocmask(SIG_BLOCK, &mask, NULL) < 0)
    return;

  accum_node_cnt = accum_node_set.size();
  accum_edge_cnt = accum_edge_set.size();
  new_node_cnt = accum_node_cnt - last_node_cnt;
  new_edge_cnt = accum_edge_cnt - last_edge_cnt;

  if(coverage_fp) {
    fprintf(coverage_fp, "Visited nodes : %d (+%d)\n", \
                         accum_node_cnt, new_node_cnt);
    fprintf(coverage_fp, "Visited edges : %d (+%d)\n", \
                         accum_edge_cnt, new_edge_cnt);
    fprintf(coverage_fp, "=========================\n");
    fclose(coverage_fp);
    coverage_fp = NULL;
  }

  if (node_fp) {
    fclose(node_fp);
    node_fp = NULL;
  }

  if (edge_fp) {
    fclose(edge_fp);
    edge_fp = NULL;
  }
}

extern "C" void eclipser_log_bb(abi_ulong node) {
  abi_ulong edge;

  /* If coverage_fp is NULL, it means that eclipser_setup() is not called yet
   * This happens when QEMU is executing a dynamically linked program.
   */
  if (!coverage_fp || !node_fp || !edge_fp)
    return;

#ifdef TARGET_X86_64
  edge = (prev_node << 16) ^ node;
#else
  edge = (prev_node << 8) ^ node;
#endif
  prev_node = node;

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

}
