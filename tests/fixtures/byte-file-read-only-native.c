#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static int opens, closes, live_streams, live_states, writes, block_writes, flushes, seeks;
static int fail_open, fail_allocate, fail_seek, fail_close, fail_read;
static FILE* read_only_stream;
static char last_mode[8];
static FILE* tracked_open(const char* path, const char* mode)
{
    ++opens;
    snprintf(last_mode, sizeof(last_mode), "%s", mode);
    if (fail_open) return NULL;
    FILE* stream = fopen(path, mode);
    if (stream) { ++live_streams; if (strcmp(mode, "rb") == 0) read_only_stream = stream; }
    return stream;
}
static int tracked_close(FILE* stream)
{
    int was_read_only = stream == read_only_stream;
    int result = fclose(stream);
    if (was_read_only) read_only_stream = NULL;
    ++closes; --live_streams;
    return fail_close ? EOF : result;
}
static void* tracked_allocate(size_t size)
{
    if (fail_allocate) return NULL;
    void* state = malloc(size);
    if (state) ++live_states;
    return state;
}
static void tracked_free(void* state) { if (state) --live_states; free(state); }
/* These hooks deliberately do not invoke incompatible stdio on an rb stream.
   Counters detect a missing capability guard without executing undefined I/O. */
static int tracked_write(int byte, FILE* stream)
{
    ++writes;
    return stream == read_only_stream ? EOF : fputc(byte, stream);
}
static size_t tracked_block_write(const void* data, size_t size, size_t count, FILE* stream)
{
    ++block_writes;
    return stream == read_only_stream ? 0 : fwrite(data, size, count, stream);
}
static int tracked_flush(FILE* stream) { ++flushes; return stream == read_only_stream ? EOF : fflush(stream); }
static int tracked_seek(FILE* stream, long offset, int origin) { ++seeks; return fail_seek ? -1 : fseek(stream, offset, origin); }
static int tracked_read(FILE* stream) { return fail_read ? EOF : fgetc(stream); }
static size_t tracked_block_read(void* data, size_t size, size_t count, FILE* stream) { return fail_read ? 0 : fread(data, size, count, stream); }
static int tracked_error(FILE* stream) { return fail_read || ferror(stream); }
#define fopen tracked_open
#define fclose tracked_close
#define malloc tracked_allocate
#define free tracked_free
#define fputc tracked_write
#define fwrite tracked_block_write
#define fflush tracked_flush
#define fseek tracked_seek
#define fgetc tracked_read
#define fread tracked_block_read
#define ferror tracked_error
#include "@READ_HEADER@"
#include "@BLOCK_HEADER@"
#undef fopen
#undef fclose
#undef malloc
#undef free
#undef fputc
#undef fwrite
#undef fflush
#undef fseek
#undef fgetc
#undef fread
#undef ferror

static int assertions;
#define CHECK(condition, name) do { if (!(condition)) { fprintf(stderr, "[FAIL] %s\n", name); return 1; } ++assertions; } while (0)

int main(int argc, char** argv)
{
    if (argc != 3) return 2;
    size_t length = strlen(argv[1]), missing_length = strlen(argv[2]);
    CHECK(length > 0 && length < 260 && missing_length > 0 && missing_length < 260, "fixture_paths_fit");
    unsigned char path[260] = {0}, missing[260] = {0};
    memcpy(path, argv[1], length); memcpy(missing, argv[2], missing_length);
    SageByteBufferState* source = sage_byte_buffer_create(256);
    SageByteBufferState* target = sage_byte_buffer_create(300);
    CHECK(source && target && sage_byte_buffer_capacity(target) == 300, "buffers_created");
    int filled = 1;
    for (int index = 0; index < 256; ++index)
        if (!sage_byte_buffer_push(source, (unsigned char)index)) filled = 0;
    CHECK(filled, "source_initialized");
    SageByteFileState* file = sage_byte_file_create_bytes(path, length);
    CHECK(file && sage_byte_file_can_write(file) && strcmp(last_mode, "w+b") == 0, "create_retains_update_mode");
    CHECK(sage_byte_file_write(file, 0) && sage_byte_file_seek_start(file), "update_byte_output_allowed");
    CHECK(sage_byte_file_write_buffer(file, source, 256) == 256 && !sage_byte_file_transfer_failed(file), "update_block_output_allowed");
    CHECK(!sage_byte_file_transfer_eof(file) && sage_byte_file_flush(file), "update_flush_allowed");
    CHECK(sage_byte_file_close(file) == 0, "update_owner_closed");
    sage_byte_file_drop(file);
    int before = opens;
    CHECK(sage_byte_file_open_read_bytes(NULL, 1) == NULL, "read_path_null_rejected");
    CHECK(sage_byte_file_open_read_bytes(path, 0) == NULL, "read_path_zero_rejected");
    CHECK(sage_byte_file_open_read_bytes(path, 260) == NULL, "read_path_oversized_rejected");
    unsigned char saved = path[0]; path[0] = 0;
    CHECK(sage_byte_file_open_read_bytes(path, length) == NULL, "read_path_embedded_zero_rejected");
    path[0] = 255;
    CHECK(sage_byte_file_open_read_bytes(path, length) == NULL, "read_path_non_ascii_rejected");
    path[0] = saved;
    CHECK(opens == before, "invalid_read_paths_never_open");
    CHECK(sage_byte_file_open_read_bytes(missing, missing_length) == NULL && strcmp(last_mode, "rb") == 0, "missing_path_fails_in_read_mode");
    fail_open = 1;
    CHECK(sage_byte_file_open_read_bytes(path, length) == NULL && live_streams == 0, "read_open_failure");
    fail_open = 0; fail_allocate = 1; before = closes;
    CHECK(sage_byte_file_open_read_bytes(path, length) == NULL, "read_state_allocation_failure");
    CHECK(live_streams == 0 && live_states == 2 && closes == before + 1, "allocation_failure_closes_input_stream");
    fail_allocate = 0;
    file = sage_byte_file_open_read_bytes(path, length);
    CHECK(file && sage_byte_file_is_open(file) && !sage_byte_file_can_write(file) && strcmp(last_mode, "rb") == 0, "existing_file_read_only_mode");
    CHECK(!sage_byte_file_can_write(NULL), "empty_cannot_write");
    CHECK(sage_byte_file_read(file) == 0, "opening_does_not_truncate");
    int old_writes = writes, old_block_writes = block_writes, old_flushes = flushes, old_seeks = seeks;
    CHECK(!sage_byte_file_write(file, 99), "read_only_byte_write_denied");
    CHECK(sage_byte_file_write_buffer(file, source, 1) == 0 && sage_byte_file_transfer_failed(file), "read_only_block_write_denied");
    CHECK(sage_byte_file_write_buffer(file, source, 0) == 0 && sage_byte_file_transfer_failed(file), "read_only_zero_write_denied");
    CHECK(!sage_byte_file_transfer_eof(file), "denied_write_not_eof");
    CHECK(!sage_byte_file_flush(file), "read_only_flush_denied");
    CHECK(writes == old_writes && block_writes == old_block_writes && flushes == old_flushes && seeks == old_seeks, "denied_output_never_reaches_stdio");
    CHECK(sage_byte_file_read(file) == 1, "denied_output_preserves_position");
    CHECK(sage_byte_file_seek_start(file), "input_rewind_allowed");
    CHECK(sage_byte_file_read_buffer(file, target, 300) == 256 && sage_byte_file_transfer_eof(file) && !sage_byte_file_transfer_failed(file), "read_only_all_bytes_and_eof");
    int matched = 1;
    for (size_t index = 0; index < 256; ++index)
        if (sage_byte_buffer_read(target, index) != (int)index) matched = 0;
    CHECK(matched && sage_byte_buffer_length(target) == 256, "all_input_bytes_preserved");
    CHECK(sage_byte_file_read(file) == -1, "read_only_clean_byte_eof");
    sage_byte_buffer_clear(target);
    CHECK(sage_byte_buffer_push(target, 77) && sage_byte_buffer_write(target, 0, 77), "target_sentinel");
    fail_seek = 1;
    CHECK(sage_byte_file_read_buffer(file, target, 1) == 0 && sage_byte_file_transfer_failed(file), "read_only_position_failure");
    CHECK(sage_byte_buffer_length(target) == 1 && sage_byte_buffer_read(target, 0) == 77, "position_failure_preserves_buffer");
    fail_seek = 0; fail_read = 1;
    CHECK(sage_byte_file_read_buffer(file, target, 1) == 0 && sage_byte_file_transfer_failed(file), "read_only_native_read_failure");
    CHECK(sage_byte_buffer_length(target) == 0, "native_input_failure_resets_prefix");
    fail_read = 0;
    CHECK(sage_byte_file_seek_start(file) && sage_byte_file_read(file) == 0, "input_recovery_after_errors");
    fail_close = 1; before = closes;
    CHECK(sage_byte_file_close(file) == EOF && !sage_byte_file_is_open(file), "failed_close_still_consumes_input");
    CHECK(!sage_byte_file_can_write(file), "closed_owner_cannot_write");
    sage_byte_file_drop(file);
    CHECK(closes == before + 1 && live_streams == 0 && live_states == 2, "failed_input_close_not_retried");
    fail_close = 0; before = closes;
    file = sage_byte_file_open_read_bytes(path, length);
    CHECK(file != NULL, "open_for_implicit_drop");
    sage_byte_file_drop(file);
    CHECK(closes == before + 1 && live_streams == 0, "input_drop_closes_stream");
    sage_byte_buffer_drop(source); sage_byte_buffer_drop(target);
    CHECK(live_states == 0, "all_native_allocations_released");
    printf("Native read-only file checks passed: %d assertions.\n", assertions);
    return 0;
}
