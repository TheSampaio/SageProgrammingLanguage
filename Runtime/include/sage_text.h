#ifndef SAGE_TEXT_H
#define SAGE_TEXT_H

#include <limits.h>
#include <stdarg.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

_Static_assert(CHAR_BIT == 8, "Sage text requires 8-bit bytes");
_Static_assert(SIZE_MAX >= UINT32_MAX, "Sage text requires at least 32-bit size_t");

/* Private storage. Only data[0..length) is user data and every byte value is data.
   One extra byte past the capacity holds a terminator that is rewritten after each
   mutation, so data is also a valid C string while it has no embedded zero byte.
   Capacity is fixed at creation. */
typedef struct SageTextState
{
    size_t length;
    size_t capacity;
    unsigned char data[];
} SageTextState;

static SageTextState* sage_text_create(size_t capacity)
{
    if (capacity > UINT32_MAX || capacity > SIZE_MAX - sizeof(SageTextState)) return NULL;
    SageTextState* state = malloc(sizeof(*state) + capacity + 1);
    if (!state) return NULL;
    state->length = 0;
    state->capacity = capacity;
    state->data[0] = '\0';
    return state;
}

/* Bounded strlen over a legacy terminated string; rejects NULL and > UINT32_MAX. */
static int sage_text_measure(const char* value, size_t* length)
{
    if (!value) return 0;
    size_t count = 0;
    while (value[count] != '\0')
    {
        if (count == UINT32_MAX) return 0;
        ++count;
    }
    *length = count;
    return 1;
}

static SageTextState* sage_text_from_cstr(const char* value)
{
    size_t length;
    if (!sage_text_measure(value, &length)) return NULL;
    SageTextState* state = sage_text_create(length);
    if (!state) return NULL;
    memcpy(state->data, value, length);
    state->length = length;
    state->data[length] = '\0';
    return state;
}

/* Copies initialized bytes into a new owner with the same capacity. */
static SageTextState* sage_text_copy(const SageTextState* source)
{
    if (!source) return NULL;
    SageTextState* state = sage_text_create(source->capacity);
    if (!state) return NULL;
    memcpy(state->data, source->data, source->length);
    state->length = source->length;
    state->data[state->length] = '\0';
    return state;
}

static size_t sage_text_length(const SageTextState* state)
{
    return state ? state->length : 0;
}

static size_t sage_text_capacity(const SageTextState* state)
{
    return state ? state->capacity : 0;
}

/* -1 is unavailable; all 256 nonnegative byte values are valid data. */
static int sage_text_byte_at(const SageTextState* state, size_t index)
{
    if (!state || index >= state->length) return -1;
    return state->data[index];
}

/* Appends are all-or-nothing: failure preserves contents and length. */
static int sage_text_append_bytes(SageTextState* state, const unsigned char* bytes, size_t length)
{
    if (!state || length > state->capacity - state->length) return 0;
    if (length > 0) memmove(state->data + state->length, bytes, length);
    state->length += length;
    state->data[state->length] = '\0';
    return 1;
}

static int sage_text_append_cstr(SageTextState* state, const char* value)
{
    size_t length;
    if (!sage_text_measure(value, &length)) return 0;
    return sage_text_append_bytes(state, (const unsigned char*)value, length);
}

static int sage_text_append(SageTextState* state, const SageTextState* other)
{
    if (!other) return 0;
    return sage_text_append_bytes(state, other->data, other->length);
}

static int sage_text_push(SageTextState* state, unsigned char byte)
{
    return sage_text_append_bytes(state, &byte, 1);
}

static void sage_text_clear(SageTextState* state)
{
    if (!state) return;
    state->length = 0;
    state->data[0] = '\0';
}

/* Lexicographic unsigned-byte order; empty owners compare as empty text. */
static int sage_text_compare_bytes(const unsigned char* left, size_t left_length, const unsigned char* right, size_t right_length)
{
    size_t shared = left_length < right_length ? left_length : right_length;
    int order = shared > 0 ? memcmp(left, right, shared) : 0;
    if (order != 0) return order < 0 ? -1 : 1;
    if (left_length == right_length) return 0;
    return left_length < right_length ? -1 : 1;
}

static int sage_text_compare(const SageTextState* left, const SageTextState* right)
{
    return sage_text_compare_bytes(left ? left->data : NULL, sage_text_length(left),
        right ? right->data : NULL, sage_text_length(right));
}

/* -2 rejects an invalid legacy string; otherwise -1, 0 or 1. */
static int sage_text_compare_cstr(const SageTextState* left, const char* right)
{
    size_t length;
    if (!sage_text_measure(right, &length)) return -2;
    return sage_text_compare_bytes(left ? left->data : NULL, sage_text_length(left), (const unsigned char*)right, length);
}

/* Writes initialized bytes (and an optional newline) to stdout. Empty owners write nothing. */
static int sage_text_print(const SageTextState* state, int newline)
{
    size_t length = sage_text_length(state);
    if (length > 0 && fwrite(state->data, 1, length, stdout) != length) return 0;
    if (newline && fputc('\n', stdout) == EOF) return 0;
    return 1;
}

/* Line-read outcome flags, decoded by the Sage wrapper. */
enum { SAGE_TEXT_LINE_EOF = 1, SAGE_TEXT_LINE_TRUNCATED = 2, SAGE_TEXT_LINE_FAILED = 4 };

/* Replaces contents with one stdin line without its terminator ("\n" or "\r\n").
   Bytes beyond capacity are consumed and discarded (truncated). EOF before a
   newline sets the EOF flag; an empty owner or stream error sets FAILED. */
static int sage_text_read_line(SageTextState* state)
{
    if (!state) return SAGE_TEXT_LINE_FAILED;
    state->length = 0;
    int flags = 0;
    int stored_carriage = 0;  /* the last stored byte is '\r' */
    int pending_carriage = 0; /* a '\r' that did not fit: terminator or lost data */
    for (;;)
    {
        int value = getchar();
        if (value == EOF)
        {
            flags |= SAGE_TEXT_LINE_EOF;
            if (pending_carriage) flags |= SAGE_TEXT_LINE_TRUNCATED;
            if (ferror(stdin)) flags |= SAGE_TEXT_LINE_FAILED;
            break;
        }
        if (value == '\n')
        {
            if (stored_carriage) --state->length;
            break;
        }
        if (pending_carriage) flags |= SAGE_TEXT_LINE_TRUNCATED;
        stored_carriage = 0;
        pending_carriage = 0;
        if (state->length < state->capacity)
        {
            state->data[state->length++] = (unsigned char)value;
            stored_carriage = value == '\r';
        }
        else if (value == '\r') pending_carriage = 1;
        else flags |= SAGE_TEXT_LINE_TRUNCATED;
    }
    state->data[state->length] = '\0';
    return flags;
}

/* Borrowed C string view of the contents: valid until the next mutation or drop, and
   shorter than length if the data holds a zero byte. Empty owners read as "". */
static const char* sage_text_cstr(const SageTextState* state)
{
    return state ? (const char*)state->data : "";
}

/* Keeps a NULL legacy string from reaching "%s". */
static const char* sage_text_cstr_or_empty(const char* value)
{
    return value ? value : "";
}

/* Interpolation target: formats into a new owner sized exactly to the result, so there
   is no shared buffer and no truncation. Arguments are evaluated once by the caller.
   Returns NULL (an empty owner) on a formatting error, allocation failure, or a result
   larger than UINT32_MAX bytes. */
static SageTextState* sage_text_format(const char* format, ...)
{
    va_list measure, write;
    va_start(measure, format);
    va_copy(write, measure);
    int needed = vsnprintf(NULL, 0, format, measure);
    va_end(measure);
    SageTextState* state = needed < 0 ? NULL : sage_text_create((size_t)needed);
    if (state)
    {
        if (vsnprintf((char*)state->data, (size_t)needed + 1, format, write) == needed) state->length = (size_t)needed;
        else
        {
            free(state);
            state = NULL;
        }
    }
    va_end(write);
    return state;
}

static void sage_text_drop(SageTextState* state)
{
    free(state);
}

#endif
