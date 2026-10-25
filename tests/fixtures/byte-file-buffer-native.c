#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>

static size_t read_max = SIZE_MAX, write_max = SIZE_MAX;
static int force_error, force_eof, fail_seek;
static int read_calls, write_calls, seek_calls;
static size_t tracked_read(void* data, size_t size, size_t count, FILE* stream)
{
    ++read_calls;
    return fread(data, size, count < read_max ? count : read_max, stream);
}
static size_t tracked_write(const void* data, size_t size, size_t count, FILE* stream)
{
    ++write_calls;
    return fwrite(data, size, count < write_max ? count : write_max, stream);
}
static int tracked_seek(FILE* stream, long offset, int origin)
{
    ++seek_calls;
    return fail_seek ? -1 : fseek(stream, offset, origin);
}
static int tracked_error(FILE* stream) { return force_error || ferror(stream); }
static int tracked_eof(FILE* stream) { return force_eof || feof(stream); }
#define fread tracked_read
#define fwrite tracked_write
#define fseek tracked_seek
#define ferror tracked_error
#define feof tracked_eof
#include "@BLOCK_HEADER@"
#undef fread
#undef fwrite
#undef fseek
#undef ferror
#undef feof

static int assertions;
#define CHECK(condition, name) do { if (!(condition)) { fprintf(stderr, "[FAIL] %s\n", name); return 1; } ++assertions; } while (0)
#define STATUS(file, end, error) (sage_byte_file_transfer_eof(file) == (end) && sage_byte_file_transfer_failed(file) == (error))

int main(int argc, char** argv)
{
    if (argc != 2) return 2;
    CHECK(sage_byte_file_create_bytes(NULL, 1) == NULL, "invalid_path_does_not_open");
    SageByteFileState* file = sage_byte_file_create(argv[1]);
    CHECK(file && sage_byte_file_is_open(file) && STATUS(file, 0, 0), "file_created_status_initialized");
    SageByteBufferState* source = sage_byte_buffer_create(256);
    SageByteBufferState* target = sage_byte_buffer_create(300);
    SageByteBufferState* zero = sage_byte_buffer_create(0);
    CHECK(source && target && zero, "buffers_allocated");
    CHECK(sage_byte_buffer_capacity(target) == 300, "target_capacity");
    int pushed = 1;
    for (int byte = 0; byte < 256; ++byte)
        if (!sage_byte_buffer_push(source, (unsigned char)byte)) pushed = 0;
    CHECK(pushed && sage_byte_buffer_length(source) == 256, "all_bytes_initialized");
    CHECK(sage_byte_buffer_push(target, 77), "target_sentinel_initialized");
    CHECK(sage_byte_file_write(file, 0) && sage_byte_file_flush(file), "legacy_output_still_works");
    CHECK(sage_byte_file_seek_start(file) && sage_byte_file_read(file) == 0, "legacy_input_still_works");
    CHECK(sage_byte_file_seek_start(file), "position_for_blocks");

    int reads_before = read_calls, writes_before = write_calls, seeks_before = seek_calls;
    CHECK(sage_byte_file_read_buffer(file, target, 301) == 0 && STATUS(file, 0, 1), "read_over_capacity_rejected");
    CHECK(sage_byte_file_read_buffer(file, target, SIZE_MAX) == 0 && STATUS(file, 0, 1), "read_extreme_limit_rejected");
    CHECK(sage_byte_file_write_buffer(file, source, 257) == 0 && STATUS(file, 0, 1), "write_over_length_rejected");
    CHECK(sage_byte_file_write_buffer(file, source, SIZE_MAX) == 0 && STATUS(file, 0, 1), "write_extreme_limit_rejected");
    CHECK(sage_byte_file_read_buffer(file, NULL, 1) == 0 && STATUS(file, 0, 1), "null_target_rejected");
    CHECK(sage_byte_file_write_buffer(file, NULL, 1) == 0 && STATUS(file, 0, 1), "null_source_rejected");
    CHECK(read_calls == reads_before && write_calls == writes_before && seek_calls == seeks_before, "invalid_requests_do_no_io");
    CHECK(sage_byte_buffer_length(target) == 1 && sage_byte_buffer_read(target, 0) == 77, "invalid_reads_preserve_target");
    CHECK(sage_byte_file_read_buffer(NULL, target, 1) == 0 && STATUS(NULL, 0, 1), "null_file_read");
    CHECK(sage_byte_file_write_buffer(NULL, source, 1) == 0 && STATUS(NULL, 0, 1), "null_file_write");
    CHECK(sage_byte_file_read_buffer(file, target, 0) == 0 && STATUS(file, 0, 0), "zero_read_success");
    CHECK(sage_byte_file_write_buffer(file, source, 0) == 0 && STATUS(file, 0, 0), "zero_write_success");
    CHECK(sage_byte_file_read_buffer(file, zero, 0) == 0 && STATUS(file, 0, 0), "zero_capacity_zero_read");
    CHECK(sage_byte_file_write_buffer(file, zero, 0) == 0 && STATUS(file, 0, 0), "empty_prefix_zero_write");
    CHECK(read_calls == reads_before && write_calls == writes_before && seek_calls == seeks_before, "zero_requests_do_no_io");
    CHECK(sage_byte_buffer_length(target) == 1 && sage_byte_buffer_read(target, 0) == 77, "zero_read_preserves_target");
    CHECK(sage_byte_file_read_buffer(file, zero, 1) == 0 && STATUS(file, 0, 1), "positive_read_zero_capacity_rejected");
    CHECK(sage_byte_file_write_buffer(file, zero, 1) == 0 && STATUS(file, 0, 1), "uninitialized_source_rejected");

    CHECK(sage_byte_file_write_buffer(file, source, 256) == 256 && STATUS(file, 0, 0), "full_block_output");
    CHECK(sage_byte_buffer_length(source) == 256 && sage_byte_buffer_read(source, 255) == 255, "output_preserves_source");
    CHECK(sage_byte_file_seek_start(file), "rewind_for_exact_read");
    CHECK(sage_byte_file_read_buffer(file, target, 256) == 256 && STATUS(file, 0, 0), "exact_read_does_not_predict_eof");
    CHECK(sage_byte_buffer_length(target) == 256, "read_replaces_length");
    int matched = 1;
    for (size_t index = 0; index < 256; ++index)
        if (sage_byte_buffer_read(target, index) != (int)index) matched = 0;
    CHECK(matched, "all_256_bytes_roundtrip");
    CHECK(sage_byte_file_read_buffer(file, target, 300) == 0 && STATUS(file, 1, 0), "clean_eof_zero_count");
    CHECK(sage_byte_buffer_length(target) == 0 && sage_byte_buffer_read(target, 0) == -1, "eof_hides_old_contents");
    CHECK(sage_byte_file_read_buffer(file, target, 0) == 0 && STATUS(file, 0, 0), "zero_read_does_not_probe_existing_eof");
    CHECK(sage_byte_file_seek_start(file), "rewind_for_short_eof");
    CHECK(sage_byte_file_read_buffer(file, target, 300) == 256 && STATUS(file, 1, 0), "partial_clean_eof_count");
    CHECK(sage_byte_buffer_length(target) == 256 && sage_byte_buffer_read(target, 256) == -1, "short_read_exposes_only_initialized_prefix");

    CHECK(sage_byte_file_seek_start(file), "rewind_for_partial_error");
    read_max = 2; force_error = 1;
    CHECK(sage_byte_file_read_buffer(file, target, 6) == 2 && STATUS(file, 0, 1), "partial_read_failure_keeps_count");
    CHECK(sage_byte_buffer_length(target) == 2 && sage_byte_buffer_read(target, 1) == 1 && sage_byte_buffer_read(target, 2) == -1, "partial_error_prefix_only");
    read_max = 0;
    CHECK(sage_byte_file_read_buffer(file, target, 6) == 0 && STATUS(file, 0, 1), "zero_read_error_distinct_from_eof");
    CHECK(sage_byte_buffer_length(target) == 0, "native_read_error_replaces_old_length");
    read_max = 2; force_eof = 1;
    CHECK(sage_byte_file_read_buffer(file, target, 6) == 2 && STATUS(file, 1, 1), "read_preserves_both_native_flags");
    force_eof = 0; force_error = 0;
    CHECK(sage_byte_file_read_buffer(file, target, 6) == 2 && STATUS(file, 0, 1), "unexplained_short_read_is_failure");
    read_max = SIZE_MAX;
    CHECK(sage_byte_file_read_buffer(file, target, 6) == 6 && STATUS(file, 0, 0), "status_resets_after_prior_failure");

    sage_byte_buffer_clear(target);
    CHECK(sage_byte_buffer_push(target, 77), "restore_sentinel_before_position_failure");
    fail_seek = 1; reads_before = read_calls; writes_before = write_calls;
    CHECK(sage_byte_file_read_buffer(file, target, 6) == 0 && STATUS(file, 0, 1), "read_position_failure");
    CHECK(sage_byte_file_write_buffer(file, source, 6) == 0 && STATUS(file, 0, 1), "write_position_failure");
    CHECK(read_calls == reads_before && write_calls == writes_before, "position_failure_blocks_native_transfer");
    CHECK(sage_byte_buffer_length(target) == 1 && sage_byte_buffer_read(target, 0) == 77, "position_failure_preserves_target");
    fail_seek = 0;

    CHECK(sage_byte_buffer_write(source, 0, 99) && sage_byte_buffer_write(source, 1, 98) && sage_byte_buffer_write(source, 2, 97), "prepare_partial_output");
    CHECK(sage_byte_file_seek_start(file), "rewind_for_partial_output");
    write_max = 3;
    CHECK(sage_byte_file_write_buffer(file, source, 8) == 3 && STATUS(file, 0, 1), "partial_write_count_even_without_error_flag");
    CHECK(sage_byte_buffer_length(source) == 256 && sage_byte_buffer_read(source, 0) == 99, "partial_output_preserves_source");
    write_max = 0;
    CHECK(sage_byte_file_write_buffer(file, source, 8) == 0 && STATUS(file, 0, 1), "zero_short_write_is_failure");
    write_max = SIZE_MAX; force_error = 1;
    CHECK(sage_byte_file_seek_start(file), "rewind_for_full_count_error");
    CHECK(sage_byte_file_write_buffer(file, source, 4) == 4 && STATUS(file, 0, 1), "full_write_count_can_report_error");
    force_error = 0;
    CHECK(sage_byte_file_flush(file), "flush_block_output");
    CHECK(sage_byte_file_close(file) == 0, "consuming_close_after_blocks");
    reads_before = read_calls; writes_before = write_calls;
    CHECK(sage_byte_file_read_buffer(file, target, 1) == 0 && STATUS(file, 0, 1), "closed_file_read_failure");
    CHECK(sage_byte_file_write_buffer(file, source, 1) == 0 && STATUS(file, 0, 1), "closed_file_write_failure");
    CHECK(read_calls == reads_before && write_calls == writes_before, "closed_file_no_native_transfer");
    CHECK(sage_byte_buffer_length(target) == 1 && sage_byte_buffer_read(target, 0) == 77, "closed_file_preserves_target");
    sage_byte_file_drop(file);
    sage_byte_buffer_drop(source);
    sage_byte_buffer_drop(target);
    sage_byte_buffer_drop(zero);
    printf("Native block I/O checks passed: %d assertions.\n", assertions);
    return 0;
}
