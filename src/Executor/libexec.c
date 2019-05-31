/*
    For forkserver related parts, modified codes from AFL's QEMU mode (original
    license below).
    ---------------------------------------------------------------------
    Forkserver written and design by Michal Zalewski <lcamtuf@google.com>
    and Jann Horn <jannhorn@googlemail.com>

    Copyright 2013, 2014, 2015, 2016 Google Inc. All rights reserved.

    Licensed under the Apache License, Version 2.0 (the "License");
    you may not use this file except in compliance with the License.
    You may obtain a copy of the License at:

      http://www.apache.org/licenses/LICENSE-2.0
*/


#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>
#include <fcntl.h>
#include <signal.h>
#include <errno.h>
#include <dlfcn.h>
#include <pty.h>
#include <sys/shm.h>
#include <sys/time.h>
#include <sys/wait.h>
#include <sys/resource.h>
#include <stdint.h>

#define PATH_FORKSRV_FD      198
#define FEED_FORKSRV_FD      194
#define FORK_WAIT_MULT  10

static int shm_id;
static pid_t path_forksrv_pid;
static int path_fsrv_ctl_fd, path_fsrv_st_fd;
static pid_t feed_forksrv_pid;
static int feed_fsrv_ctl_fd, feed_fsrv_st_fd;

static pid_t child_pid = 0;
static int pty_fd;
static int pty_flag;
static int replay_flag;
static int timeout_flag;
static int non_fork_stdin_fd;
static int path_stdin_fd;
static int feed_stdin_fd;

pid_t (*sym_forkpty)(int *, char *, const struct termios*, const struct winsize *);

void error_exit(char* msg) {
    perror(msg);
    exit(-1);
}

void set_env (char *env_variable, char *env_value) {
    setenv(env_variable, env_value, 1);
}

void unset_env (char *env_variable) {
    unsetenv(env_variable);
}

void open_stdin_fd(int * fd){

    unlink(".stdin");

    *fd = open(".stdin", O_RDWR | O_CREAT | O_EXCL, 0600);

    if (*fd == -1)
        error_exit("open_stdin_fd : failed to open");

    /* If the descriptor is leaked, program will consume all the file
     * descriptors up to *_FORKSRV_FD, which results in protocol error
     * between forkserver and its client.
     */
    if (*fd > PATH_FORKSRV_FD - 10)
        error_exit("open_stdin_fd : detected a leak of file descriptor");
}

void write_stdin(int stdin_fd, int stdin_size, char* stdin_data) {
    lseek(stdin_fd, 0, SEEK_SET);

    if(write(stdin_fd, stdin_data, stdin_size) != stdin_size)
        error_exit("write_stdin");

    if(ftruncate(stdin_fd, stdin_size))
        error_exit("ftruncate");

    lseek(stdin_fd, 0, SEEK_SET);
}

static void alarm_callback(int sig) {

    if (child_pid) {
        puts("Timeout");
        fflush(stdout);
        if (replay_flag) {
          /* If we are replaying test cases, we should use GDB to quit the
           * executed program. Otherwise, gcov data will not be generated,
           * and coverage cannot be obtained correctly. Codes are borrowed
           * from klee_replay.c of KLEE project.
           */
          int status;
          char pid_buf[64];
          pid_t gdb_pid = fork();
          if (gdb_pid < 0) {
            puts("[Warning] Failed to fork() for GDB execution");
            fflush(stdout);
          } else if (gdb_pid == 0) { // child process
            char *gdb_args[] = {
              "/usr/bin/gdb", "--pid", pid_buf, "-q", "--batch",
              "--eval-command=call exit(1)", NULL };
            sprintf(pid_buf, "%d", child_pid);
            execvp(gdb_args[0], gdb_args);
            puts("[Warning] GDB execution failed");
            fflush(stdout);
          } else { // parent process, wait until GDB is executed
            usleep(250 * 1000); // Give 250ms for GDB to work
            /* Sometimes child process for GDB hangs, so we should send
             * SIGKILL signal. */
            if ( waitpid( gdb_pid, &status, WNOHANG) < 0 )
              kill(gdb_pid, SIGKILL);
          }
        } else {
          /* If we are in fuzzing mode, send SIGTERM (not SIGKILL) so that QEMU
           * tracer can receive it and call eclipser_exit() to log feedback.
           */
          kill(child_pid, SIGTERM);
        }

        timeout_flag = 1;
        /* In some cases, the child process may not be terminated by the code
         * above, so examine if process is alive and send SIGKILL if so. */
        usleep(400 * 1000); // Give 400ms for SIGTERM handling
        if ( kill(child_pid, 0) == 0)
          kill(child_pid, SIGKILL);
    }
}

void initialize_exec (int is_replay) {
    struct sigaction sa;
    void* handle = dlopen("libutil.so.1", RTLD_LAZY);

    sym_forkpty = (pid_t (*)(int *, char*, const struct termios*, const struct winsize*))dlsym(handle, "forkpty");

    sa.sa_flags     = SA_RESTART;
    sa.sa_sigaction = NULL;

    sigemptyset(&sa.sa_mask);

    replay_flag = is_replay;
    sa.sa_handler = alarm_callback;
    sigaction(SIGALRM, &sa, NULL);
}

int waitchild(pid_t pid, uint64_t timeout)
{
    int childstatus = 0;

    if (timeout >= 1000)
        alarm(timeout/1000);
    else
        ualarm(timeout*1000, 0);

    if ( waitpid(pid, &childstatus, 0) < 0)
      perror("[Warning] waitpid() : ");

    alarm(0); // Cancle pending alarm
    if (pty_flag)
        close(pty_fd);

    if ( WIFEXITED( childstatus ) ) return 0;

    if ( WIFSIGNALED( childstatus ) ) {
        if ( WTERMSIG( childstatus ) == SIGSEGV ) return SIGSEGV;
        else if ( WTERMSIG( childstatus ) == SIGFPE ) return SIGFPE;
        else if ( WTERMSIG( childstatus ) == SIGILL ) return SIGILL;
        else if ( WTERMSIG( childstatus ) == SIGABRT ) return SIGABRT;
        else if ( timeout_flag ) return SIGALRM;
        else return 0;
    } else {
        return 0;
    }
}

void nonblocking_stdin() {
    int flags;
    if ((flags = fcntl(0, F_GETFL, 0)) == -1)
      flags = 0;
    if (fcntl(0, F_SETFL, flags | O_NONBLOCK) == -1) {
      perror("fcntl");
      return;
    }
}

void term_setting(int pty_fd) {
    struct termios newtio;

    tcgetattr(pty_fd, &newtio);
    newtio.c_lflag &= ~ICANON & ~ECHO;
    newtio.c_cc[VINTR]    = 0;     /* Ctrl-c */
    newtio.c_cc[VQUIT]    = 0;     /* Ctrl-\ */
    newtio.c_cc[VSUSP]    = 0;     /* Ctrl-z */
    tcsetattr(pty_fd, TCSADRAIN, &newtio);
}

int exec(int argc, char **args, int stdin_size, char *stdin_data, uint64_t timeout, int use_pty) {
    int i, devnull, ret;
    char **argv = (char **)malloc( sizeof(char*) * (argc + 1) );

    if (!argv) error_exit( "args malloc" );

    for (i = 0; i<argc; i++)
        argv[i] = args[i];
    argv[i] = 0;

    if (use_pty) {
        pty_flag = 1;
        child_pid = sym_forkpty(&pty_fd, NULL, NULL, NULL);
    } else {
        pty_flag = 0;
        child_pid = vfork();
    }
    if (child_pid == 0) {
        devnull = open( "/dev/null", O_RDWR );
        if ( devnull < 0 ) error_exit( "devnull open" );
        dup2(devnull, 1);
        dup2(devnull, 2);
        close(devnull);

        if (pty_flag) {
            nonblocking_stdin();
        } else {
            open_stdin_fd(&non_fork_stdin_fd);
            write_stdin(non_fork_stdin_fd, stdin_size, stdin_data);
            dup2(non_fork_stdin_fd, 0);
            // We already wrote stdin_data and redirected it, so OK to close
            close(non_fork_stdin_fd);
        }

        execv(argv[0], argv);
        exit(-1);
    } else if (child_pid > 0) {
        if (pty_flag) {
            term_setting(pty_fd);
            if(write(pty_fd, stdin_data, stdin_size) != stdin_size)
                error_exit("exec() : write(pty_fd, ...)");
        }

        free(argv);
    } else {
        error_exit("fork");
    }

    timeout_flag = 0; // Reset timeout_flag

    return waitchild(child_pid, timeout);
}

pid_t init_forkserver(int argc, char** args, uint64_t timeout, int forksrv_fd,
                      int *stdin_fd, int *fsrv_ctl_fd, int *fsrv_st_fd) {
    static struct itimerval it;
    int st_pipe[2], ctl_pipe[2];
    int status;
    int devnull, i;
    int32_t rlen;
    pid_t forksrv_pid;
    char **argv = (char **)malloc( sizeof(char*) * (argc + 1) );

    open_stdin_fd(stdin_fd);

    if (!argv) error_exit( "args malloc" );
    for (i = 0; i<argc; i++)
        argv[i] = args[i];
    argv[i] = 0;

    if (pipe(st_pipe) || pipe(ctl_pipe)) error_exit("pipe() failed");

    forksrv_pid = fork();

    if (forksrv_pid < 0) error_exit("fork() failed");

    if (!forksrv_pid) {

        struct rlimit r;

        if (!getrlimit(RLIMIT_NOFILE, &r) && r.rlim_cur < PATH_FORKSRV_FD + 2) {
          r.rlim_cur = PATH_FORKSRV_FD + 2;
          setrlimit(RLIMIT_NOFILE, &r); /* Ignore errors */
        }

        r.rlim_max = r.rlim_cur = 0;

        setrlimit(RLIMIT_CORE, &r); /* Ignore errors */

        setsid();

        devnull = open( "/dev/null", O_RDWR );
        if ( devnull < 0 ) error_exit( "devnull open" );
        dup2(devnull, 1);
        dup2(devnull, 2);
        close(devnull);

        dup2(*stdin_fd, 0);

      if (dup2(ctl_pipe[0], forksrv_fd) < 0) error_exit("dup2() failed");
      if (dup2(st_pipe[1], forksrv_fd + 1) < 0) error_exit("dup2() failed");

      close(ctl_pipe[0]);
      close(ctl_pipe[1]);
      close(st_pipe[0]);
      close(st_pipe[1]);

      setenv("LD_BIND_NOW", "1", 0);

      setenv("ASAN_OPTIONS", "abort_on_error=1:"
                             "detect_leaks=0:"
                             "symbolize=0:"
                             "allocator_may_return_null=1", 0);

      execv(argv[0], argv);

      exit(0);
    }
    free(argv);

    close(ctl_pipe[0]);
    close(st_pipe[1]);

    *fsrv_ctl_fd = ctl_pipe[1];
    *fsrv_st_fd  = st_pipe[0];

    it.it_value.tv_sec = (timeout * FORK_WAIT_MULT) / 1000;
    it.it_value.tv_usec = ((timeout * FORK_WAIT_MULT) % 1000) * 1000;

    setitimer(ITIMER_REAL, &it, NULL);

    rlen = read(*fsrv_st_fd, &status, 4);

    it.it_value.tv_sec = 0;
    it.it_value.tv_usec = 0;

    setitimer(ITIMER_REAL, &it, NULL);

    if (rlen == 4) {
      return forksrv_pid;
    }

    if (timeout_flag) {
      perror("Timeout while initializing fork server");
      return -1;
    }

    if (waitpid(forksrv_pid, &status, 0) <= 0) {
      perror("waitpid() failed while initializing fork server");
      return -1;
    }

    perror("Fork server died");
    return -1;
}

pid_t init_forkserver_coverage(int argc, char** args, uint64_t timeout) {
    path_forksrv_pid = init_forkserver(argc, args, timeout, PATH_FORKSRV_FD,
                       &path_stdin_fd, &path_fsrv_ctl_fd, &path_fsrv_st_fd);
    return path_forksrv_pid;
}

pid_t init_forkserver_branch(int argc, char** args, uint64_t timeout) {
    feed_forksrv_pid = init_forkserver(argc, args, timeout, FEED_FORKSRV_FD,
                       &feed_stdin_fd, &feed_fsrv_ctl_fd, &feed_fsrv_st_fd);
    return feed_forksrv_pid;
}

void kill_forkserver() {

    close(path_stdin_fd);
    close(feed_stdin_fd);

    close(path_fsrv_ctl_fd);
    close(path_fsrv_st_fd);
    close(feed_fsrv_ctl_fd);
    close(feed_fsrv_st_fd);

    if (path_forksrv_pid) {
        kill(path_forksrv_pid, SIGKILL);
        path_forksrv_pid = 0;
    }
    if (feed_forksrv_pid) {
        kill(feed_forksrv_pid, SIGKILL);
        feed_forksrv_pid = 0;
    }
}

int exec_fork_coverage(uint64_t timeout, int stdin_size, char *stdin_data) {
    int res, childstatus;
    static struct itimerval it;

    int run_mode = atoi(getenv("CK_MODE"));

    write_stdin(path_stdin_fd, stdin_size, stdin_data);

    if ((res = write(path_fsrv_ctl_fd, &run_mode, 4)) != 4) {
      perror("exec_fork_coverage: Cannot request new process to fork server");
      printf("write() call ret = %d\n", res);
      return -1;
    }

    if ((res = read(path_fsrv_st_fd, &child_pid, 4)) != 4) {
      perror("exec_fork_coverage: Failed to receive child pid from fork server");
      printf("read() call ret = %d, child_pid = %d\n", res, child_pid);
      return -1;
    }

    if (child_pid <= 0) {
      perror("exec_fork_coverage: Fork server is mibehaving");
      return -1;
    }

    it.it_value.tv_sec = (timeout / 1000);
    it.it_value.tv_usec = (timeout % 1000) * 1000;
    setitimer(ITIMER_REAL, &it, NULL);

    if ((res = read(path_fsrv_st_fd, &childstatus, 4)) != 4) {
      perror("exec_fork_coverage: Unable to communicate with fork server");
      printf("read() call ret = %d, childstatus = %d\n", res, childstatus);
      return -1;
    }

    if (!WIFSTOPPED(childstatus)) child_pid = 0;

    it.it_value.tv_sec = 0;
    it.it_value.tv_usec = 0;
    setitimer(ITIMER_REAL, &it, NULL);

    if ( WIFEXITED( childstatus ) ) return 0;

    if ( WIFSIGNALED( childstatus ) ) {
        if ( WTERMSIG( childstatus ) == SIGSEGV ) return SIGSEGV;
        else if ( WTERMSIG( childstatus ) == SIGFPE ) return SIGFPE;
        else if ( WTERMSIG( childstatus ) == SIGILL ) return SIGILL;
        else if ( WTERMSIG( childstatus ) == SIGABRT ) return SIGABRT;
        else if ( timeout_flag ) return SIGALRM;
        else return 0;
    } else {
        return 0;
    }
}

int exec_fork_branch(uint64_t timeout, int stdin_size, char *stdin_data) {
    uint64_t targ_addr, targ_index;
    int res, childstatus;
    static struct itimerval it;

    targ_addr = strtol(getenv("CK_FEED_ADDR"), NULL, 16);
    targ_index = strtol(getenv("CK_FEED_IDX"), NULL, 16);
    /* TODO : what if we want to use pseudo-terminal? */
    write_stdin(feed_stdin_fd, stdin_size, stdin_data);

    if ((res = write(feed_fsrv_ctl_fd, &targ_addr, 8)) != 8) {
      perror("exec_fork_branch: Cannot request new process to fork server (1)");
      printf("write() call ret = %d\n", res);
      return -1;
    }

    if ((res = write(feed_fsrv_ctl_fd, &targ_index, 8)) != 8) {
      perror("exec_fork_branch: Cannot request new process to fork server (2)");
      printf("write() call ret = %d\n", res);
      return -1;
    }

    if ((res = read(feed_fsrv_st_fd, &child_pid, 4)) != 4) {
      perror("exec_fork_branch: Failed to receive child pid from fork server");
      printf("read() call ret = %d, child_pid = %d\n", res, child_pid);
      return -1;
    }

    if (child_pid <= 0) {
      perror("exec_fork_branch: Fork server is mibehaving");
      return -1;
    }

    it.it_value.tv_sec = (timeout / 1000);
    it.it_value.tv_usec = (timeout % 1000) * 1000;
    setitimer(ITIMER_REAL, &it, NULL);

    if ((res = read(feed_fsrv_st_fd, &childstatus, 4)) != 4) {
      perror("exec_fork_branch: Unable to communicate with fork server");
      printf("read() call ret = %d, childstatus = %d\n", res, childstatus);
      return -1;
    }

    if (!WIFSTOPPED(childstatus)) child_pid = 0;

    it.it_value.tv_sec = 0;
    it.it_value.tv_usec = 0;
    setitimer(ITIMER_REAL, &it, NULL);

    if ( WIFEXITED( childstatus ) ) return 0;

    if ( WIFSIGNALED( childstatus ) ) {
        if ( WTERMSIG( childstatus ) == SIGSEGV ) return SIGSEGV;
        else if ( WTERMSIG( childstatus ) == SIGFPE ) return SIGFPE;
        else if ( WTERMSIG( childstatus ) == SIGILL ) return SIGILL;
        else if ( WTERMSIG( childstatus ) == SIGABRT ) return SIGABRT;
        else if ( timeout_flag ) return SIGALRM;
        else return 0;
    } else {
        return 0;
    }
}

int prepare_shared_mem(void) {
  shm_id = shmget(IPC_PRIVATE, 0x10000, IPC_CREAT | IPC_EXCL | 0600);
  return shm_id;
}

int release_shared_mem(void) {
  return shmctl(shm_id, IPC_RMID, 0);
}
