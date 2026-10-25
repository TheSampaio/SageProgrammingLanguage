#include <stdio.h>
#include <stdlib.h>

static int live_streams, live_states, close_calls, free_calls;
static int fail_open, fail_allocate, fail_seek, fail_read, fail_write, fail_flush, fail_close;

static FILE* tracked_open(const char* path, const char* mode)
{
    if (fail_open) return NULL;
    FILE* stream = fopen(path, mode);
    if (stream) ++live_streams;
    return stream;
}
static int tracked_close(FILE* stream)
{
    int result = fclose(stream);
    --live_streams;
    ++close_calls;
    return fail_close ? EOF : result;
}
static void* tracked_allocate(size_t size)
{
    if (fail_allocate) return NULL;
    void* state = malloc(size);
    if (state) ++live_states;
    return state;
}
static void tracked_free(void* state)
{
    if (state) { --live_states; ++free_calls; }
    free(state);
}
static int tracked_seek(FILE* stream, long offset, int origin)
{
    return fail_seek ? -1 : fseek(stream, offset, origin);
}
static int tracked_read(FILE* stream) { return fail_read ? EOF : fgetc(stream); }
static int tracked_error(FILE* stream) { return fail_read ? 1 : ferror(stream); }
static int tracked_write(int byte, FILE* stream) { return fail_write ? EOF : fputc(byte, stream); }
static int tracked_flush(FILE* stream) { return fail_flush ? EOF : fflush(stream); }

#define fopen tracked_open
#define fclose tracked_close
#define malloc tracked_allocate
#define free tracked_free
#define fseek tracked_seek
#define fgetc tracked_read
#define ferror tracked_error
#define fputc tracked_write
#define fflush tracked_flush
#include "@BYTE_HEADER@"
#undef fopen
#undef fclose
#undef malloc
#undef free
#undef fseek
#undef fgetc
#undef ferror
#undef fputc
#undef fflush

static int assertions;
#define CHECK(condition, name) do { if (!(condition)) { fprintf(stderr, "[FAIL] %s\n", name); return 1; } ++assertions; } while (0)

int main(int argc, char** argv)
{
    if (argc != 2) return 2;
    CHECK(!sage_byte_file_is_open(NULL), "empty_is_closed");
    CHECK(sage_byte_file_read(NULL) == -2, "empty_read_error");
    CHECK(!sage_byte_file_write(NULL, 255), "empty_write_error");
    CHECK(!sage_byte_file_seek_start(NULL), "empty_seek_error");
    CHECK(!sage_byte_file_flush(NULL), "empty_flush_error");
    CHECK(sage_byte_file_close(NULL) == EOF, "empty_close_error");
    sage_byte_file_drop(NULL);
    CHECK(live_states == 0 && close_calls == 0, "null_drop_no_work");
    CHECK(sage_byte_file_create(NULL) == NULL, "null_path_rejected");

    fail_open = 1;
    CHECK(sage_byte_file_create(argv[1]) == NULL && live_streams == 0 && live_states == 0, "open_failure_no_leak");
    fail_open = 0;
    fail_allocate = 1;
    CHECK(sage_byte_file_create(argv[1]) == NULL, "allocation_failure_return");
    CHECK(live_streams == 0 && live_states == 0 && close_calls == 1, "allocation_failure_closes_stream");
    fail_allocate = 0;

    SageByteFileState* state = sage_byte_file_create(argv[1]);
    CHECK(state && sage_byte_file_is_open(state), "created_file_open");
    CHECK(live_streams == 1 && live_states == 1, "exclusive_state_and_stream");
    CHECK(sage_byte_file_read(state) == -1, "empty_file_eof");
    CHECK(sage_byte_file_write(state, 0), "write_zero_after_input");
    CHECK(sage_byte_file_write(state, 255), "write_ff");
    CHECK(sage_byte_file_flush(state), "flush_output");
    CHECK(sage_byte_file_seek_start(state), "seek_start");
    CHECK(sage_byte_file_read(state) == 0, "read_zero");
    CHECK(sage_byte_file_flush(state), "flush_after_input");
    CHECK(sage_byte_file_read(state) == 255, "read_ff_is_not_eof");
    CHECK(sage_byte_file_read(state) == -1, "clean_eof");
    CHECK(sage_byte_file_write(state, 42), "output_after_eof");
    CHECK(sage_byte_file_seek_start(state), "seek_for_update");
    CHECK(sage_byte_file_read(state) == 0, "positioned_read");
    CHECK(sage_byte_file_write(state, 7), "output_after_non_eof_input");
    CHECK(sage_byte_file_seek_start(state), "seek_verify_update");
    CHECK(sage_byte_file_read(state) == 0 && sage_byte_file_read(state) == 7 && sage_byte_file_read(state) == 42, "updated_contents");

    fail_seek = 1;
    CHECK(sage_byte_file_read(state) == -2, "position_failure_blocks_read");
    CHECK(!sage_byte_file_write(state, 8), "position_failure_blocks_write");
    CHECK(!sage_byte_file_flush(state), "position_failure_blocks_flush");
    CHECK(!sage_byte_file_seek_start(state), "seek_failure_status");
    fail_seek = 0;
    fail_read = 1;
    CHECK(sage_byte_file_read(state) == -2, "read_error_distinct_from_eof");
    fail_read = 0;
    fail_write = 1;
    CHECK(!sage_byte_file_write(state, 8), "write_error_status");
    fail_write = 0;
    fail_flush = 1;
    CHECK(!sage_byte_file_flush(state), "flush_error_status");
    fail_flush = 0;

    CHECK(sage_byte_file_close(state) == 0, "explicit_close_success");
    CHECK(!sage_byte_file_is_open(state) && live_streams == 0, "explicit_close_consumes_stream");
    CHECK(sage_byte_file_read(state) == -2 && !sage_byte_file_write(state, 1), "closed_state_blocks_io");
    CHECK(sage_byte_file_close(state) == EOF && close_calls == 2, "closed_stream_not_reclosed");
    sage_byte_file_drop(state);
    CHECK(live_states == 0 && free_calls == 1 && close_calls == 2, "drop_after_close_only_frees_state");

    state = sage_byte_file_create(argv[1]);
    CHECK(state != NULL, "create_for_close_failure");
    fail_close = 1;
    CHECK(sage_byte_file_close(state) == EOF && live_streams == 0, "close_failure_still_consumes_stream");
    sage_byte_file_drop(state);
    CHECK(live_states == 0 && close_calls == 3 && free_calls == 2, "failed_close_not_retried_by_drop");
    fail_close = 0;
    state = sage_byte_file_create(argv[1]);
    CHECK(state != NULL, "create_for_implicit_drop");
    sage_byte_file_drop(state);
    CHECK(live_streams == 0 && live_states == 0 && close_calls == 4 && free_calls == 3, "implicit_drop_closes_and_frees");
    printf("Native byte file checks passed: %d assertions.\n", assertions);
    return 0;
}
