#include <stdint.h>
#include <sys/mman.h>
#include <sys/shm.h>
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

#define MODE_COUNT_NEW  0 // Count newly covered edges, along with path hash.
#define MODE_HASH       1 // Calculate edge set hash.
#define MODE_SET        2 // Return the set of the visited edges.
int chatkey_mode = -1;

static uint32_t new_edge_cnt = 0; // # of new edges visited in this execution
static abi_ulong edge_set_hash = 5381; // djb2 hash
static abi_ulong path_hash = 5381; // djb2 hash
static abi_ulong prev_node = 0;
/* Global file pointers */
static FILE* coverage_fp;
static FILE* dbg_fp;

static int is_fp_closed = 0;

static unsigned char * accum_edge_bitmap;
static unsigned char edge_bitmap[0x10000];
// Holds edges visited in this exec (will be dumped into a file)
static dense_hash_set<abi_ulong> edge_set;

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

extern "C" void chatkey_setup_before_forkserver(void) {
  char * shm_id;

  shm_id = getenv("CK_SHM_ID");
  assert(shm_id != NULL);
  accum_edge_bitmap = (unsigned char *) shmat(atoi(shm_id), NULL, 0);
  assert(accum_edge_bitmap != (void *) -1);

  edge_set.set_empty_key(0);
}

extern "C" void chatkey_setup_after_forkserver(void) {
  char * dbg_path = getenv("CK_DBG_LOG");
  char * coverage_path = getenv("CK_COVERAGE_LOG");

  if (chatkey_mode == -1) {
    assert(getenv("CK_MODE") != NULL);
    chatkey_mode = atoi(getenv("CK_MODE"));
  }
  //assert(getenv("CK_FORK_SERVER") != NULL);
  //// If fork server is enabled, chatkey_mode should have been set already.
  //if (atoi(getenv("CK_FORK_SERVER")) == 0) {
  //  assert(getenv("CK_MODE") != NULL);
  //  chatkey_mode = atoi(getenv("CK_MODE"));
  //}

  /* Open file pointers and descriptors early, since if we try to open them in
   * chatkey_exit(), it gets mixed with stderr & stdout stream. This seems to
   * be an issue due to incorrect file descriptor management in QEMU code.
   */

  assert(coverage_path != NULL);
  coverage_fp = fopen(coverage_path, "w");
  assert(coverage_fp != NULL);

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
  fclose(coverage_fp);

  if (afl_forksrv_pid)
      close(TSL_FD);
}

extern "C" void chatkey_exit(void) {
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

    /* Output new edge # and path hash. */
    fprintf(coverage_fp, "%d\n", new_edge_cnt);
#ifdef TARGET_X86_64
    fprintf(coverage_fp, "%lu\n", path_hash);
    fprintf(coverage_fp, "%lu\n", edge_set_hash);
#else
    fprintf(coverage_fp, "%u\n", path_hash);
    fprintf(coverage_fp, "%u\n", edge_set_hash);
#endif

    fclose(coverage_fp);
  } else if (chatkey_mode == MODE_HASH) {
    /* Output path hash and edge hash */
#ifdef TARGET_X86_64
    fprintf(coverage_fp, "%lu\n", edge_set_hash);
#else
    fprintf(coverage_fp, "%u\n", edge_set_hash);
#endif
    fclose(coverage_fp);
  } else if (chatkey_mode == MODE_SET) {
    /* Dump visited edge set */
    dump_set(&edge_set, coverage_fp);
    fclose(coverage_fp);
  } else {
    assert(false);
  }

  if (dbg_fp)
    fclose(dbg_fp);
}

static inline void chatkey_update_path_hash(register abi_ulong addr) {
    register unsigned int i;
    for (i = 0; i < sizeof(abi_ulong); i++)
        path_hash = ((path_hash << 5) + path_hash) + ((addr >> (i<<3)) & 0xff);
}

static inline void update_edge_set_hash(register abi_ulong edge_hash) {
  edge_set_hash = edge_set_hash ^ edge_hash;
}

extern "C" void chatkey_log_bb(abi_ulong addr, abi_ulong callsite) {
    abi_ulong edge, hash;
    unsigned int byte_idx, byte_mask;
    unsigned char old_byte, new_byte;

    chatkey_update_path_hash(addr);
    edge = addr;
#ifdef TARGET_X86_64
    edge = (prev_node << 16) ^ addr;
#else
    edge = (prev_node << 8) ^ addr;
#endif
    prev_node = addr;

    if (chatkey_mode == MODE_COUNT_NEW) {
      // Check and update both edge_bitmap and accumulative bitmap
      hash = (edge >> 4) ^ (edge << 8);
      byte_idx = (hash >> 3) & 0xffff;
      byte_mask = 1 << (hash & 0x7); // Use the lowest 3 bits to shift
      old_byte = edge_bitmap[byte_idx];
      new_byte = old_byte | byte_mask;
      if (old_byte != new_byte) {
        edge_bitmap[byte_idx] = new_byte;
        // If it's a new edge, update edge hash and also add to accumulative map
        update_edge_set_hash(hash);
        old_byte = accum_edge_bitmap[byte_idx];
        new_byte = old_byte | byte_mask;
        if (old_byte != new_byte) {
          new_edge_cnt++;
          accum_edge_bitmap[byte_idx] = new_byte;
          /* Log visited addrs if dbg_fp is not NULL */
          if (dbg_fp) {
#ifdef TARGET_X86_64
            fprintf(dbg_fp, "(0x%lx, 0x%lx)\n", addr, callsite);
#else
            fprintf(dbg_fp, "(0x%x, 0x%x)\n", addr, callsite);
#endif
          }
        }
      }
    } else if (chatkey_mode == MODE_HASH) {
      // Check and update edge_bitmap only
      hash = (edge >> 4) ^ (edge << 8);
      byte_idx = (hash >> 3) & 0xffff;
      byte_mask = 1 << (hash & 0x7); // Lowest 3 bits
      old_byte = edge_bitmap[byte_idx];
      new_byte = old_byte | byte_mask;
      if (old_byte != new_byte) {
        edge_bitmap[byte_idx] = new_byte;
        // If it's a new edge, update edge hash and also add to accumulative map
        update_edge_set_hash(hash);
      }
    } else if (chatkey_mode == MODE_SET ) {
      // Just insert currently covered edge to the edge set
      edge_set.insert(edge);
    } else if (chatkey_mode != -1) {
      /* If chatkey_mode is -1, it means that chatkey_setup() is not called yet
       * This happens when QEMU is executing a dynamically linked program. Other
       * values mean error.
       */
      assert(false);
    }
}
