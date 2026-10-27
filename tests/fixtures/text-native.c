#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static int allocations, releases, live, fail_allocate;
static void* tracked_allocate(size_t size)
{
    ++allocations;
    if (fail_allocate) return NULL;
    void* state = malloc(size);
    if (state) { ++live; memset(state, 0xA5, size); }
    return state;
}
static void tracked_free(void* state)
{
    if (state) { --live; ++releases; }
    free(state);
}
#define malloc tracked_allocate
#define free tracked_free
#include "@TEXT_HEADER@"
#undef malloc
#undef free

static int assertions;
#define CHECK(condition, name) do { if (!(condition)) { fprintf(stderr, "[FAIL] %s\n", name); return 1; } ++assertions; } while (0)

static int same(const SageTextState* state, const char* expected)
{
    return sage_text_compare_cstr(state, expected) == 0;
}

/* Feeds bytes to stdin through a temporary file, then reads one line. */
static int read_from(const char* path, const char* bytes, size_t length, SageTextState* state)
{
    FILE* input = fopen(path, "wb");
    if (!input || fwrite(bytes, 1, length, input) != length || fclose(input) != 0) return -1;
    if (!freopen(path, "rb", stdin)) return -1;
    return sage_text_read_line(state);
}

int main(int argc, char** argv)
{
    if (argc < 3) return 2;
    const char* path = argv[1];

    CHECK(sage_text_length(NULL) == 0 && sage_text_capacity(NULL) == 0, "null_reads_empty");
    CHECK(sage_text_byte_at(NULL, 0) == -1, "null_byte_unavailable");
    CHECK(!sage_text_append_cstr(NULL, "x") && !sage_text_push(NULL, 1), "null_append_fails");
    CHECK(sage_text_compare(NULL, NULL) == 0 && sage_text_compare_cstr(NULL, "") == 0, "null_compares_as_empty");
    CHECK(sage_text_compare_cstr(NULL, NULL) == -2, "null_cstr_rejected");
    CHECK(sage_text_copy(NULL) == NULL && sage_text_from_cstr(NULL) == NULL, "null_sources_rejected");
    CHECK(sage_text_read_line(NULL) == SAGE_TEXT_LINE_FAILED, "null_read_line_fails");
    sage_text_clear(NULL);
    sage_text_drop(NULL);
    CHECK(allocations == 0 && live == 0, "null_operations_allocate_nothing");

    fail_allocate = 1;
    CHECK(sage_text_create(8) == NULL && sage_text_from_cstr("abc") == NULL && live == 0, "allocation_failure_empty");
    fail_allocate = 0;
    int previous = allocations;
    CHECK(sage_text_create(SIZE_MAX) == NULL && allocations == previous, "overflow_rejected_before_allocation");
    if (SIZE_MAX > UINT32_MAX)
        CHECK(sage_text_create((size_t)UINT32_MAX + 1) == NULL && allocations == previous, "capacity_limit_enforced_natively");

    SageTextState* text = sage_text_create(6);
    CHECK(text && sage_text_length(text) == 0 && sage_text_capacity(text) == 6, "create_empty");
    CHECK(sage_text_byte_at(text, 0) == -1, "spare_capacity_unreadable");
    CHECK(sage_text_append_cstr(text, "abc") && same(text, "abc"), "append_cstr");
    CHECK(!sage_text_append_cstr(text, "defg") && same(text, "abc"), "append_is_all_or_nothing");
    CHECK(sage_text_push(text, 0) && sage_text_byte_at(text, 3) == 0 && sage_text_length(text) == 4, "nul_byte_is_data");
    CHECK(sage_text_push(text, 255) && sage_text_byte_at(text, 4) == 255, "high_byte_is_data");
    CHECK(sage_text_compare_cstr(text, "abc") == 1, "embedded_nul_not_terminator");
    sage_text_clear(text);
    CHECK(sage_text_length(text) == 0 && sage_text_capacity(text) == 6 && sage_text_byte_at(text, 0) == -1, "clear_hides_old_bytes");
    CHECK(sage_text_append_cstr(text, "ab") && sage_text_append(text, text) && same(text, "abab"), "self_append_natively_safe");
    CHECK(!sage_text_append(text, text) && same(text, "abab"), "self_append_overflow_preserves");

    SageTextState* copy = sage_text_copy(text);
    CHECK(copy && copy != text && sage_text_capacity(copy) == 6 && sage_text_compare(copy, text) == 0, "copy_independent_equal");
    CHECK(sage_text_push(copy, 'z') && same(text, "abab"), "copy_does_not_alias");
    CHECK(sage_text_compare(text, copy) == -1 && sage_text_compare(copy, text) == 1, "compare_prefix_order");
    SageTextState* high = sage_text_from_cstr("\xff");
    SageTextState* low = sage_text_from_cstr("a");
    CHECK(high && low && sage_text_compare(low, high) == -1, "compare_unsigned_bytes");
    SageTextState* none = sage_text_from_cstr("");
    CHECK(none && sage_text_capacity(none) == 0 && sage_text_compare(none, NULL) == 0, "empty_literal_valid_owner");
    CHECK(!sage_text_append(text, NULL), "append_from_null_fails");

    SageTextState* line = sage_text_create(4);
    CHECK(line != NULL, "line_owner");
    int flags = read_from(path, "ab\r\nrest", 8, line);
    CHECK(flags == 0 && same(line, "ab"), "line_crlf_removed");
    flags = sage_text_read_line(line);
    CHECK(flags == SAGE_TEXT_LINE_EOF && same(line, "rest"), "line_eof_without_newline");
    flags = sage_text_read_line(line);
    CHECK(flags == SAGE_TEXT_LINE_EOF && sage_text_length(line) == 0, "line_eof_empty");
    flags = read_from(path, "abcdef\nnext\n", 12, line);
    CHECK(flags == SAGE_TEXT_LINE_TRUNCATED && same(line, "abcd"), "line_truncated_consumes_rest");
    flags = sage_text_read_line(line);
    CHECK(flags == 0 && same(line, "next"), "line_after_truncation_starts_fresh");
    flags = read_from(path, "ab\r\r\n", 5, line);
    CHECK(flags == 0 && same(line, "ab\r"), "line_only_final_carriage_removed");
    flags = read_from(path, "abcd\r\n", 6, line);
    CHECK(flags == 0 && same(line, "abcd"), "line_exact_fit_crlf_not_truncated");
    flags = read_from(path, "abcd\rx\n", 7, line);
    CHECK(flags == SAGE_TEXT_LINE_TRUNCATED && same(line, "abcd"), "line_overflow_carriage_is_data");
    flags = read_from(path, "abcd\r", 5, line);
    CHECK(flags == (SAGE_TEXT_LINE_TRUNCATED | SAGE_TEXT_LINE_EOF) && same(line, "abcd"), "line_overflow_carriage_at_eof");
    flags = read_from(path, "\n", 1, line);
    CHECK(flags == 0 && sage_text_length(line) == 0, "line_empty_line");

    /* Output goes to a file so the exact bytes can be checked; stdout is not restored. */
    if (!freopen(argv[2], "wb", stdout)) return 3;
    int printed = sage_text_print(copy, 1) && sage_text_print(none, 0) && fflush(stdout) == 0;
    FILE* written = fopen(argv[2], "rb");
    char bytes[16] = { 0 };
    size_t count = written ? fread(bytes, 1, sizeof(bytes), written) : 0;
    if (written) fclose(written);
    CHECK(printed && count == 6 && memcmp(bytes, "ababz\n", 6) == 0, "print_writes_exact_bytes");

    /* Formatting sizes the owner exactly, keeps no shared buffer, and keeps a C terminator. */
    SageTextState* formatted = sage_text_format("%s-%lld-%.2f", sage_text_cstr_or_empty(NULL), 7LL, 1.5);
    CHECK(formatted && sage_text_length(formatted) == 7 && sage_text_capacity(formatted) == 7 &&
        strcmp(sage_text_cstr(formatted), "-7-1.50") == 0, "format_exact_capacity");
    SageTextState* second = sage_text_format("%lld", 42LL);
    CHECK(second && strcmp(sage_text_cstr(formatted), "-7-1.50") == 0 && strcmp(sage_text_cstr(second), "42") == 0, "format_results_independent");
    SageTextState* nothing = sage_text_format("");
    CHECK(nothing && sage_text_length(nothing) == 0 && sage_text_capacity(nothing) == 0 && sage_text_cstr(nothing)[0] == 0, "format_empty");
    fail_allocate = 1;
    CHECK(sage_text_format("%d", 1) == NULL, "format_allocation_failure");
    fail_allocate = 0;
    CHECK(strcmp(sage_text_cstr(NULL), "") == 0 && strcmp(sage_text_cstr_or_empty(NULL), "") == 0, "cstr_of_empty_owner");
    SageTextState* terminated = sage_text_create(3);
    CHECK(terminated && sage_text_push(terminated, 'a') && strcmp(sage_text_cstr(terminated), "a") == 0, "terminator_after_push");
    sage_text_clear(terminated);
    CHECK(strcmp(sage_text_cstr(terminated), "") == 0, "terminator_after_clear");
    sage_text_drop(terminated);
    sage_text_drop(nothing);
    sage_text_drop(second);
    sage_text_drop(formatted);

    sage_text_drop(text);
    sage_text_drop(copy);
    sage_text_drop(high);
    sage_text_drop(low);
    sage_text_drop(none);
    sage_text_drop(line);
    CHECK(live == 0 && releases == 10, "every_owner_released_once");
    /* Success is silent: stdout now points at the output file. */
    return assertions > 0 ? 0 : 4;
    return 0;
}
