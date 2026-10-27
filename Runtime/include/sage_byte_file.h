#ifndef SAGE_BYTE_FILE_H
#define SAGE_BYTE_FILE_H

#include <limits.h>
#include <stdio.h>
#include <stdlib.h>

_Static_assert(CHAR_BIT == 8, "Sage byte files require 8-bit bytes");

/* Trusted native state. Sage sees only the declared opaque owning resource. */
typedef struct SageByteFileState
{
    FILE* stream;
    int transfer_eof;
    int transfer_failed;
    int writable;
} SageByteFileState;

/* Internal constructors fix the native mode and its allowed operations together. */
static SageByteFileState* sage_byte_file_open(const char* path, const char* mode, int writable)
{
    if (!path) return NULL;
    FILE* stream = fopen(path, mode);
    if (!stream) return NULL;
    SageByteFileState* state = malloc(sizeof(*state));
    if (!state) { (void)fclose(stream); return NULL; }
    state->stream = stream;
    state->transfer_eof = 0;
    state->transfer_failed = 0;
    state->writable = writable != 0;
    return state;
}

static SageByteFileState* sage_byte_file_create(const char* path)
{
    return sage_byte_file_open(path, "w+b", 1);
}

/* Caller supplies 260 readable bytes. Validate the selected prefix before I/O. */
static int sage_byte_file_copy_path(const unsigned char path[260], size_t length, char native_path[260])
{
    if (!path || length == 0 || length >= 260) return 0;
    for (size_t index = 0; index < length; ++index)
    {
        unsigned char byte = path[index];
        if (byte < 32 || byte > 126) return 0;
        native_path[index] = (char)byte;
    }
    native_path[length] = '\0';
    return 1;
}

static SageByteFileState* sage_byte_file_create_bytes(const unsigned char path[260], size_t length)
{
    char native_path[260];
    if (!sage_byte_file_copy_path(path, length, native_path)) return NULL;
    return sage_byte_file_create(native_path);
}

static int sage_byte_file_is_open(const SageByteFileState* state)
{
    return state && state->stream;
}

static int sage_byte_file_can_write(const SageByteFileState* state)
{
    return sage_byte_file_is_open(state) && state->writable;
}

/* Positioning separates C update-stream input/output, including input -> flush. */
static int sage_byte_file_prepare(SageByteFileState* state)
{
    if (!sage_byte_file_is_open(state)) return 0;
    clearerr(state->stream);
    return fseek(state->stream, 0L, SEEK_CUR) == 0;
}

/* 0..255 is data; -1 is clean EOF; -2 is failure (including an empty owner). */
static int sage_byte_file_read(SageByteFileState* state)
{
    if (!sage_byte_file_prepare(state)) return -2;
    int value = fgetc(state->stream);
    if (value != EOF) return value;
    return ferror(state->stream) ? -2 : -1;
}

static int sage_byte_file_write(SageByteFileState* state, unsigned char value)
{
    if (!sage_byte_file_can_write(state) || !sage_byte_file_prepare(state)) return 0;
    return fputc((int)value, state->stream) != EOF;
}

static int sage_byte_file_seek_start(SageByteFileState* state)
{
    if (!sage_byte_file_is_open(state)) return 0;
    clearerr(state->stream);
    return fseek(state->stream, 0L, SEEK_SET) == 0;
}

static int sage_byte_file_flush(SageByteFileState* state)
{
    if (!sage_byte_file_can_write(state) || !sage_byte_file_prepare(state)) return 0;
    return fflush(state->stream) == 0;
}

static int sage_byte_file_close(SageByteFileState* state)
{
    if (!sage_byte_file_is_open(state)) return EOF;
    FILE* stream = state->stream;
    state->stream = NULL; /* fclose consumes the stream even when it reports failure. */
    return fclose(stream);
}

static void sage_byte_file_drop(SageByteFileState* state)
{
    if (!state) return;
    if (state->stream) (void)sage_byte_file_close(state);
    free(state);
}

#endif
