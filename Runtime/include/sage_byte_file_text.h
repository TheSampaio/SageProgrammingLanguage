#ifndef SAGE_BYTE_FILE_TEXT_H
#define SAGE_BYTE_FILE_TEXT_H

#include "sage_byte_file.h"
#include "sage_byte_file_read_only.h"
#include "sage_text.h"

/* Paths come from owned Text and use the same 1..259 printable ASCII contract as
   the u8[260] byte paths; only text->data[0..length) is read. */
static SageByteFileState* sage_byte_file_create_text(const SageTextState* path)
{
    if (!path || path->length == 0 || path->length >= 260) return NULL;
    return sage_byte_file_create_bytes(path->data, path->length);
}

static SageByteFileState* sage_byte_file_open_read_text(const SageTextState* path)
{
    if (!path || path->length == 0 || path->length >= 260) return NULL;
    return sage_byte_file_open_read_bytes(path->data, path->length);
}

/* Writes all text bytes. Status is captured like the buffer transfers. */
static size_t sage_byte_file_write_text(SageByteFileState* file, const SageTextState* text)
{
    if (!file) return 0;
    file->transfer_eof = 0;
    file->transfer_failed = 1;
    if (!sage_byte_file_can_write(file) || !text) return 0;
    if (text->length == 0) { file->transfer_failed = 0; return 0; }
    if (!sage_byte_file_prepare(file)) return 0;
    size_t count = fwrite(text->data, 1, text->length, file->stream);
    file->transfer_failed = ferror(file->stream) != 0 || count < text->length;
    return count;
}

/* Replaces text contents with up to its capacity bytes from the current position. */
static size_t sage_byte_file_read_text(SageByteFileState* file, SageTextState* text)
{
    if (!file) return 0;
    file->transfer_eof = 0;
    file->transfer_failed = 1;
    if (!sage_byte_file_is_open(file) || !text) return 0;
    if (text->capacity == 0) { text->length = 0; file->transfer_failed = 0; return 0; }
    if (!sage_byte_file_prepare(file)) return 0;
    size_t count = fread(text->data, 1, text->capacity, file->stream);
    text->length = count;
    file->transfer_eof = feof(file->stream) != 0;
    file->transfer_failed = ferror(file->stream) != 0 || (count < text->capacity && !file->transfer_eof);
    return count;
}

#endif
