#include <stdint.h>
#include <sys/mman.h>
#include <fcntl.h>
#include <assert.h>
#include <unistd.h>
#include <signal.h>
#include <sparsehash/dense_hash_set>

using google::dense_hash_set;

#ifdef TARGET_X86_64
typedef uint64_t abi_ulong;
#else
typedef uint32_t abi_ulong;
#endif

extern unsigned int afl_forksrv_pid;
#define FORKSRV_FD 198
#define TSL_FD (FORKSRV_FD - 1)

abi_ulong chatkey_entry_point; /* ELF entry point (_start) */

#define MODE_COUNT_NEW  0 // Count newly covered nodes, along with path hash.
#define MODE_HASH       1 // Calculate node set hash.
#define MODE_SET        2 // Return the set of the visited nodes.
int chatkey_mode = -1;

static abi_ulong path_hash = 5381; // djb2 hash

/* Global file pointers */
static FILE* hash_fp;
static FILE* coverage_fp;
static FILE* node_fp;
static FILE* dbg_fp;

static int is_fp_closed = 0;

// Holds nodes visited until now (accumulative).
static dense_hash_set<abi_ulong> accum_node_set;
// Holds nodes visited in this exec (per execution).
static dense_hash_set<abi_ulong> node_set;

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

/* Calculate hash with elements in 'set'. This function is used to calculate
 * the hash of node set itself.
 */
static abi_ulong calculate_set_hash(dense_hash_set<abi_ulong> *set)
{
  abi_ulong hash = 5381;
  for (google::dense_hash_set<abi_ulong>::iterator it = set->begin();
       it != set->end();
       ++it)
  {
    register unsigned int i;
    abi_ulong elem = *it;
    for (i = 0; i < sizeof(abi_ulong); i++)
      hash = ((hash << 5) + hash) + ((elem >> (i<<3)) & 0xff);
  }
  return hash;
}

uint32_t dump_new_elems(dense_hash_set<abi_ulong> *set,
                        dense_hash_set<abi_ulong> *accum_set,
                        FILE * output_fp)
{
  uint32_t new_elem_cnt = 0;
  for(google::dense_hash_set<abi_ulong>::iterator it = set->begin();
      it != set->end();
      ++it)
  {
    abi_ulong elem = *it;
    if (accum_set->find(elem) == accum_set->end()) {
      new_elem_cnt++;
      fwrite(&elem, sizeof(abi_ulong), 1, output_fp);
    }
  }
  return new_elem_cnt;
}

static void dump_set(dense_hash_set<abi_ulong> * set, FILE* output_fp)
{
  google::dense_hash_set<abi_ulong>::iterator it;

  for (it = set->begin(); it != set->end(); ++it)
  {
    abi_ulong elem;
    elem = *it;
    fwrite(&elem, sizeof(abi_ulong), 1, output_fp);
  }
}

extern "C" void chatkey_setup(void) {
  char * dbg_path = getenv("CK_DBG_LOG");

  assert(getenv("CK_FORK_SERVER") != NULL);
  // If fork server is enabled, chatkey_mode should have been set already.
  if (atoi(getenv("CK_FORK_SERVER")) == 0) {
    assert(getenv("CK_MODE") != NULL);
    chatkey_mode = atoi(getenv("CK_MODE"));
  }

  /* Open file pointers and descriptors early, since if we try to open them in
   * chatkey_exit(), it gets mixed with stderr & stdout stream. This seems to
   * be an issue due to incorrect file descriptor management in QEMU code.
   */
  if (chatkey_mode == MODE_COUNT_NEW) {
    char * coverage_path = getenv("CK_COVERAGE_LOG");
    char * node_path = getenv("CK_NODE_LOG");

    assert(coverage_path != NULL);
    coverage_fp = fopen(coverage_path, "w");
    assert(coverage_fp != NULL);

    assert(node_path != NULL);
    node_fp = fopen(node_path, "a+");
    assert(node_fp != NULL);

    accum_node_set.set_empty_key(0);
    node_set.set_empty_key(0);
    init_accum_set(node_path, &accum_node_set);
  } else if (chatkey_mode == MODE_HASH) {
    char * hash_path = getenv("CK_HASH_LOG");

    assert(hash_path != NULL);
    hash_fp = fopen(hash_path, "w");
    assert(hash_fp != NULL);
    /* No accumulate set needed, just initialize 'node_set'. */
    node_set.set_empty_key(0);
  } else if (chatkey_mode == MODE_SET) {
    char * coverage_path = getenv("CK_COVERAGE_LOG");

    assert(coverage_path != NULL);
    coverage_fp = fopen(coverage_path, "w");
    assert(coverage_fp != NULL);
    /* No accumulate set needed, just initialize 'node_set'. */
    node_set.set_empty_key(0);
  } else {
    assert(false);
  }

  /* In dbg_path is not NULL, open the file for debug message logging. */
  if(dbg_path != NULL) {
    dbg_fp = fopen(dbg_path, "w");
    assert(dbg_fp != NULL);
  }
}

// When fork() syscall is encountered, child process should call this function
extern "C" void chatkey_close_fp(void) {

  is_fp_closed = 1;

  // close 'coverage_fp', since we don't want to dump log twice
  if (chatkey_mode == MODE_COUNT_NEW) {
    fclose(coverage_fp);
    fclose(node_fp);
  } else if (chatkey_mode == MODE_HASH) {
    fclose(hash_fp);
  } else if (chatkey_mode == MODE_SET) {
    fclose(coverage_fp);
  } else {
    assert(false);
  }

  if (afl_forksrv_pid)
      close(TSL_FD);
}

extern "C" void chatkey_exit(void) {
  uint32_t new_node_cnt; // # of new nodes visited in this execution
  static abi_ulong node_hash; // djb2 hash
  sigset_t mask;

  // If chatkey_close_fp() was called, then return without any action.
  if (is_fp_closed)
    return;

  // Block signals, since we register signal handler that calls chatkey_exit()/
  if (sigfillset(&mask) < 0)
    return;
  if (sigprocmask(SIG_BLOCK, &mask, NULL) < 0)
    return;

  if (chatkey_mode == MODE_COUNT_NEW) {

    node_hash = calculate_set_hash(&node_set);
    /* Now union current node set to accumulcative node set */
    new_node_cnt = dump_new_elems(&node_set, &accum_node_set, node_fp);

    /* Output new node # and path hash. */
    fprintf(coverage_fp, "%d\n", new_node_cnt);
#ifdef TARGET_X86_64
    fprintf(coverage_fp, "%lu\n", path_hash);
    fprintf(coverage_fp, "%lu\n", node_hash);
#else
    fprintf(coverage_fp, "%u\n", path_hash);
    fprintf(coverage_fp, "%u\n", node_hash);
#endif

    fclose(coverage_fp);
    fclose(node_fp);
  } else if (chatkey_mode == MODE_HASH) {
    /* Output path hash and node hash */

    node_hash = calculate_set_hash(&node_set);
#ifdef TARGET_X86_64
    fprintf(hash_fp, "%lu\n", node_hash);
#else
    fprintf(hash_fp, "%u\n", node_hash);
#endif
    fclose(hash_fp);
  } else if (chatkey_mode == MODE_SET) {
    /* Dump visited node set */
    dump_set(&node_set, coverage_fp);
    fclose(coverage_fp);
  } else {
    assert(false);
  }

  if (dbg_fp)
    fclose(dbg_fp);
}

extern "C" void chatkey_log_bb(abi_ulong addr, abi_ulong callsite) {
    abi_ulong node;

    if (chatkey_mode == MODE_COUNT_NEW) {
      /* First calculate the value to represent currently covered node */
#ifdef TARGET_X86_64
      node = addr ^ (callsite << 16);
#else
      node = addr ^ (callsite << 8);
#endif
      /* Before inserting, log debugging information if dbg_fp is not NULL */
      if (dbg_fp &&
          node_set.find(node) == node_set.end() && // To avoid duplication
          accum_node_set.find(node) == accum_node_set.end()) {
#ifdef TARGET_X86_64
        fprintf(dbg_fp, "(0x%lx, 0x%lx)\n", addr, callsite);
#else
        fprintf(dbg_fp, "(0x%x, 0x%x)\n", addr, callsite);
#endif
      }
      /* Now insert to the node set */
      node_set.insert(node);
    } else if (chatkey_mode == MODE_HASH || chatkey_mode == MODE_SET ) {
      /* Just insert currently covered node to the node set */
#ifdef TARGET_X86_64
      node = addr ^ (callsite << 16);
#else
      node = addr ^ (callsite << 8);
#endif
      node_set.insert(node);
    } else if (chatkey_mode != -1) {
      /* If chatkey_mode is -1, it means that chatkey_setup() is not called yet
       * This happens when QEMU is executing a dynamically linked program. Other
       * values mean error.
       */
      assert(false);
    }
}

extern "C" void chatkey_update_path_hash(register abi_ulong addr) {
    register unsigned int i;
    for (i = 0; i < sizeof(abi_ulong); i++)
        path_hash = ((path_hash << 5) + path_hash) + ((addr >> (i<<3)) & 0xff);
}
