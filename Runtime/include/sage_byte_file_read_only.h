#ifndef SAGE_BYTE_FILE_READ_ONLY_H
#define SAGE_BYTE_FILE_READ_ONLY_H

#include "sage_byte_file.h"

/* Read mode fails for missing paths and never creates/truncates their contents. */
static SageByteFileState* sage_byte_file_open_read_bytes(const unsigned char path[260], size_t length)
{
    char native_path[260];
    if (!sage_byte_file_copy_path(path, length, native_path)) return NULL;
    return sage_byte_file_open(native_path, "rb", 0);
}

#endif
